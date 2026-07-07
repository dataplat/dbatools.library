#nullable enable
#pragma warning disable CA1416 // Windows-only command: WMI, registry, certificate store

using System;
using System.Collections.Generic;
using System.Management;
using System.Management.Automation;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using Dataplat.Dbatools.Message;
using Microsoft.SqlServer.Management.Smo.Wmi;

namespace Dataplat.Dbatools.Commands;

public sealed partial class GetDbaNetworkConfigurationCommand
{
    private const uint HKEY_LOCAL_MACHINE = 0x80000002u;

    private ManagedComputer BuildManagedComputer(string computerName)
    {
        if (Credential is not null)
        {
            // ManagedComputer requires a plain-text password; this is a necessary
            // exception — the WMI API has no secure-string overload.
            System.Net.NetworkCredential netCred = Credential.GetNetworkCredential();
            string user = string.IsNullOrEmpty(netCred.Domain)
                ? netCred.UserName
                : $"{netCred.Domain}\\{netCred.UserName}";
            return new ManagedComputer(computerName, user, netCred.Password);
        }
        return new ManagedComputer(computerName);
    }

    private static object? GetProtocolPropertyValue(ServerProtocol sp, string name)
    {
        foreach (ProtocolProperty pp in sp.ProtocolProperties)
        {
            if (string.Equals(pp.Name, name, StringComparison.Ordinal))
                return pp.Value;
        }
        return null;
    }

    private static PSObject BuildTcpIpProperties(ServerProtocol wmiSpTcp)
    {
        PSObject obj = new();
        obj.Properties.Add(new PSNoteProperty("Enabled", GetProtocolPropertyValue(wmiSpTcp, "Enabled")));
        obj.Properties.Add(new PSNoteProperty("KeepAlive", GetProtocolPropertyValue(wmiSpTcp, "KeepAlive")));
        obj.Properties.Add(new PSNoteProperty("ListenAll", GetProtocolPropertyValue(wmiSpTcp, "ListenOnAllIPs")));
        return obj;
    }

    private static object[] BuildTcpIpAddresses(ServerProtocol wmiSpTcp)
    {
        // PS: TcpIpAddresses = $outputTcpIpAddressesIPn + $outputTcpIpAddressesIPAll
        // ServerProtocolIPAddress is internal — use var to iterate without naming the type.
        List<PSObject> result = new();
        PSObject? ipAll = null;

        // ServerProtocolIPAddress / its IPAddressProperties collection are internal types — use dynamic
        foreach (dynamic ipAddr in wmiSpTcp.IPAddresses)
        {
            if (string.Equals((string)ipAddr.Name, "IPAll", StringComparison.OrdinalIgnoreCase))
            {
                // IPAll has only Name, TcpDynamicPorts, TcpPort
                object? tdp = null, tp = null;
                foreach (dynamic prop in ipAddr.IPAddressProperties)
                {
                    if (prop.Name == "TcpDynamicPorts") tdp = prop.Value;
                    else if (prop.Name == "TcpPort") tp = prop.Value;
                }
                ipAll = new PSObject();
                ipAll.Properties.Add(new PSNoteProperty("Name", (string)ipAddr.Name));
                ipAll.Properties.Add(new PSNoteProperty("TcpDynamicPorts", tdp));
                ipAll.Properties.Add(new PSNoteProperty("TcpPort", tp));
            }
            else
            {
                object? active = null, enabled = null, ipAddress = null, tdp = null, tp = null;
                foreach (dynamic prop in ipAddr.IPAddressProperties)
                {
                    if (prop.Name == "Active") active = prop.Value;
                    else if (prop.Name == "Enabled") enabled = prop.Value;
                    else if (prop.Name == "IpAddress") ipAddress = prop.Value;
                    else if (prop.Name == "TcpDynamicPorts") tdp = prop.Value;
                    else if (prop.Name == "TcpPort") tp = prop.Value;
                }
                PSObject ip = new();
                ip.Properties.Add(new PSNoteProperty("Name", (string)ipAddr.Name));
                ip.Properties.Add(new PSNoteProperty("Active", active));
                ip.Properties.Add(new PSNoteProperty("Enabled", enabled));
                ip.Properties.Add(new PSNoteProperty("IpAddress", ipAddress));
                ip.Properties.Add(new PSNoteProperty("TcpDynamicPorts", tdp));
                ip.Properties.Add(new PSNoteProperty("TcpPort", tp));
                result.Add(ip);
            }
        }

        // IPn entries first, then IPAll (matching PS array concatenation order)
        if (ipAll is not null) result.Add(ipAll);
        object[] arr = new object[result.Count];
        for (int i = 0; i < result.Count; i++) arr[i] = result[i];
        return arr;
    }

    private ManagementClass GetStdRegProv(string computerName)
    {
        var opts = new ConnectionOptions();
        if (Credential is not null)
        {
            // WMI ConnectionOptions requires plain text password; no secure-string overload
            System.Net.NetworkCredential netCred = Credential.GetNetworkCredential();
            opts.Username = string.IsNullOrEmpty(netCred.Domain)
                ? netCred.UserName
                : $"{netCred.Domain}\\{netCred.UserName}";
            opts.Password = netCred.Password;
        }
        var scope = new ManagementScope($@"\\{computerName}\root\cimv2", opts);
        scope.Connect();
        return new ManagementClass(scope, new ManagementPath("StdRegProv"), null);
    }

    private static string? GetRegStringValue(ManagementClass stdReg, string subKey, string valueName)
    {
        using ManagementBaseObject inP = stdReg.GetMethodParameters("GetStringValue");
        inP["hDefKey"] = HKEY_LOCAL_MACHINE;
        inP["sSubKeyName"] = subKey;
        inP["sValueName"] = valueName;
        using ManagementBaseObject outP = stdReg.InvokeMethod("GetStringValue", inP, null);
        return outP?["sValue"]?.ToString();
    }

    private static uint? GetRegDWordValue(ManagementClass stdReg, string subKey, string valueName)
    {
        using ManagementBaseObject inP = stdReg.GetMethodParameters("GetDWORDValue");
        inP["hDefKey"] = HKEY_LOCAL_MACHINE;
        inP["sSubKeyName"] = subKey;
        inP["sValueName"] = valueName;
        using ManagementBaseObject outP = stdReg.InvokeMethod("GetDWORDValue", inP, null);
        return outP?["uValue"] as uint?;
    }

    private static byte[]? GetRegBinaryValue(ManagementClass stdReg, string subKey, string valueName)
    {
        using ManagementBaseObject inP = stdReg.GetMethodParameters("GetBinaryValue");
        inP["hDefKey"] = HKEY_LOCAL_MACHINE;
        inP["sSubKeyName"] = subKey;
        inP["sValueName"] = valueName;
        using ManagementBaseObject outP = stdReg.InvokeMethod("GetBinaryValue", inP, null);
        return outP?["uValue"] as byte[];
    }

    private static string[]? GetRegSubKeyNames(ManagementClass stdReg, string subKey)
    {
        using ManagementBaseObject inP = stdReg.GetMethodParameters("EnumKey");
        inP["hDefKey"] = HKEY_LOCAL_MACHINE;
        inP["sSubKeyName"] = subKey;
        using ManagementBaseObject outP = stdReg.InvokeMethod("EnumKey", inP, null);
        return outP?["sNames"] as string[];
    }

    private X509Certificate2? FindCertByThumbprintInRegistry(ManagementClass stdReg, string thumbprint)
    {
        // Get-ChildItem Cert:\LocalMachine -Recurse | Where-Object Thumbprint -eq $thumbprint
        string[] stores = { "MY", "ROOT", "CA" };
        string normalized = thumbprint.ToUpperInvariant();
        foreach (string store in stores)
        {
            string path = $@"SOFTWARE\Microsoft\SystemCertificates\{store}\Certificates\{normalized}";
            byte[]? blob = GetRegBinaryValue(stdReg, path, "Blob");
            if (blob is not null)
            {
                X509Certificate2? cert = LoadCertFromBlob(blob);
                if (cert is not null) return cert;
            }
        }
        return null;
    }

    /// <summary>
    /// Parse Windows CRYPT_ATTRIBUTES cert property blob.
    /// Format: [propID DWORD][reserved DWORD][cbData DWORD][data bytes]...
    /// CERT_CERT_PROP_ID = 32 contains the raw DER-encoded X.509 certificate.
    /// </summary>
    private static X509Certificate2? LoadCertFromBlob(byte[] blob)
    {
        int pos = 0;
        while (pos + 12 <= blob.Length)
        {
            uint propId = BitConverter.ToUInt32(blob, pos); pos += 4;
            pos += 4; // skip reserved
            uint cbData = BitConverter.ToUInt32(blob, pos); pos += 4;
            if (cbData > (uint)(blob.Length - pos)) break;

            if (propId == 32) // CERT_CERT_PROP_ID — raw DER-encoded certificate
            {
                byte[] certData = new byte[(int)cbData];
                Array.Copy(blob, pos, certData, 0, (int)cbData);
                try { return new X509Certificate2(certData); } catch { return null; }
            }
            pos += (int)cbData;
        }
        return null;
    }

    // PS: $suitableCertificate = Get-ChildItem ... | Where-Object {...} — an empty pipeline result
    // is $null, not an empty array. Return null (never object[0]) so the emitted property matches.
    private PSObject[]? GetSuitableCertificates(ManagementClass stdReg, string networkName)
    {
        List<PSObject> suitable = new();

        // $networkName.$env:USERDNSDOMAIN equivalent
        string dnsDomain = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().DomainName;
        string fqdn = string.IsNullOrEmpty(dnsDomain) ? networkName : $"{networkName}.{dnsDomain}";

        // For requirements see https://learn.microsoft.com/en-us/sql/database-engine/configure-windows/certificate-requirements
        X509KeyUsageFlags requiredKeyUsages = X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment;

        try
        {
            string[]? thumbprints = GetRegSubKeyNames(stdReg, @"SOFTWARE\Microsoft\SystemCertificates\MY\Certificates");
            if (thumbprints is null) return null;

            foreach (string tp in thumbprints)
            {
                byte[]? blob = GetRegBinaryValue(stdReg, $@"SOFTWARE\Microsoft\SystemCertificates\MY\Certificates\{tp}", "Blob");
                if (blob is null) continue;

                X509Certificate2? cert = LoadCertFromBlob(blob);
                if (cert is null) continue;

                try
                {
                    // Certs in the MY store are presumed to have private keys (cannot inspect remote private keys).
                    // All other PS source checks are replicated below.
                    bool keyUsagesOk = false;
                    bool ekuOk = false;
                    foreach (X509Extension ext in cert.Extensions)
                    {
                        if (ext is X509KeyUsageExtension ku)
                            keyUsagesOk = (ku.KeyUsages & requiredKeyUsages) == requiredKeyUsages;
                        else if (ext is X509EnhancedKeyUsageExtension eku)
                        {
                            foreach (Oid oid in eku.EnhancedKeyUsages)
                            {
                                if (oid.Value == "1.3.6.1.5.5.7.3.1") { ekuOk = true; break; } // Server Authentication
                            }
                        }
                    }

                    // $_.PublicKey.Oid.FriendlyName -match 'RSA'
                    bool rsaOk = cert.PublicKey.Oid.FriendlyName?.IndexOf("RSA", StringComparison.OrdinalIgnoreCase) >= 0;

                    // $_.PublicKey.Key.KeySize -ge 2048
                    bool keySizeOk = false;
                    using (RSA? rsaKey = cert.GetRSAPublicKey())
                    {
                        keySizeOk = rsaKey?.KeySize >= 2048;
                    }

                    // $_.SignatureAlgorithm.FriendlyName -match 'sha256|sha384|sha512'
                    string? sigAlg = cert.SignatureAlgorithm.FriendlyName;
                    bool sigAlgOk = sigAlg is not null && (
                        sigAlg.IndexOf("sha256", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        sigAlg.IndexOf("sha384", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        sigAlg.IndexOf("sha512", StringComparison.OrdinalIgnoreCase) >= 0);

                    bool dateOk = cert.NotBefore < DateTime.Now && cert.NotAfter > DateTime.Now;

                    // $dnsNames -contains $networkName -or $dnsNames -contains "$networkName.$env:USERDNSDOMAIN"
                    bool dnsNamesOk = false;
                    foreach (string dnsName in GetDnsNames(cert))
                    {
                        if (string.Equals(dnsName, networkName, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(dnsName, fqdn, StringComparison.OrdinalIgnoreCase))
                        {
                            dnsNamesOk = true;
                            break;
                        }
                    }

                    if (keyUsagesOk && ekuOk && rsaOk && keySizeOk && sigAlgOk && dateOk && dnsNamesOk)
                        suitable.Add(PSObject.AsPSObject(cert));
                }
                catch (Exception ex)
                {
                    WriteMessage(MessageLevel.Verbose, $"Failed to test certificate '{cert.Thumbprint}' for suitability: {ex}");
                }
            }
        }
        catch (Exception ex)
        {
            WriteMessage(MessageLevel.Verbose, $"Failed to enumerate suitable certificates: {ex}");
        }

        return suitable.Count == 0 ? null : suitable.ToArray();
    }

    private static IEnumerable<string> GetDnsNames(X509Certificate2 cert)
    {
        // PS: $dnsNames = $_.DnsNameList.Unicode; if (-not $dnsNames -and $_.Subject -match 'CN=([^,]+)')
        // { $dnsNames = @($Matches[1]) }. Collect SAN (2.5.29.17) DNS names; fall back to the Subject
        // CN only when NONE were found (a SAN present but carrying no DNS names must still fall back),
        // and match CN case-insensitively like -match.
        List<string> names = new();
        foreach (X509Extension ext in cert.Extensions)
        {
            if (ext.Oid?.Value == "2.5.29.17")
            {
                string formatted = ext.Format(false);
                foreach (string part in formatted.Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string trimmed = part.Trim();
                    if (trimmed.IndexOf("DNS Name=", StringComparison.OrdinalIgnoreCase) == 0)
                        names.Add(trimmed.Substring(9).Trim()); // "DNS Name=".Length == 9
                }
                break;
            }
        }
        if (names.Count == 0)
        {
            Match m = Regex.Match(cert.Subject, "CN=([^,]+)", RegexOptions.IgnoreCase);
            if (m.Success) names.Add(m.Groups[1].Value.Trim());
        }
        return names;
    }

    private static string[] GetDnsNamesArray(X509Certificate2 cert)
    {
        List<string> names = new();
        foreach (string n in GetDnsNames(cert)) names.Add(n);
        return names.ToArray();
    }

    private static bool? ConvertDWordToBool(uint? value)
    {
        // PS: switch (val) { 0 { $false } 1 { $true } } — NO default branch, so a missing DWORD or
        // any other value (e.g. ExtendedProtection = 2 "Required") yields $null, not $false.
        return value switch
        {
            0u => false,
            1u => true,
            _ => (bool?)null
        };
    }
}
