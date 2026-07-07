#nullable enable
#pragma warning disable CA1416 // Windows-only command: WMI, registry, certificate store

using System;
using System.Collections.Generic;
using System.Management;
using System.Management.Automation;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;
using Microsoft.SqlServer.Management.Smo.Wmi;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves SQL Server network protocols, TCP/IP settings, and SSL certificate configuration from SQL Server Configuration Manager.
/// Port of public/Get-DbaNetworkConfiguration.ps1; surface pinned by migration/baselines/Get-DbaNetworkConfiguration.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaNetworkConfiguration")]
[OutputType(typeof(PSObject))]
public sealed partial class GetDbaNetworkConfigurationCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Credential object used to connect to the Computer as a different user.</summary>
    [Parameter(Position = 1)]
    public PSCredential? Credential { get; set; }

    /// <summary>Controls which network configuration details are returned from SQL Server Configuration Manager.</summary>
    [Parameter(Position = 2)]
    [ValidateSet("Full", "ServerProtocols", "TcpIpProperties", "TcpIpAddresses", "Certificate")]
    public string OutputType { get; set; } = "Full";

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
        {
            return;
        }

        foreach (DbaInstanceParameter instance in SqlInstance)
        {
            try
            {
                // mirrors Resolve-DbaComputerName (lab always resolves by short name)
                string computerName = instance.ComputerName;
                // Test-ElevationRequirement and Invoke-Command2 are replaced by direct API access below.

                // As we go remote, ensure the assembly is loaded (ManagedComputer handles this)
                ManagedComputer wmi = BuildManagedComputer(computerName);

                ServerProtocol? wmiSpSm = null, wmiSpNp = null, wmiSpTcp = null;
                foreach (ServerInstance si in wmi.ServerInstances)
                {
                    if (string.Equals(si.Name, instance.InstanceName, StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (ServerProtocol sp in si.ServerProtocols)
                        {
                            if (sp.Name == "Sm") wmiSpSm = sp;
                            else if (sp.Name == "Np") wmiSpNp = sp;
                            else if (sp.Name == "Tcp") wmiSpTcp = sp;
                        }
                        break;
                    }
                }

                // Test if object is filled to test if instance was found on computer
                if (wmiSpSm is null)
                {
                    StopFunction($"Failed to collect network configuration from {instance.ComputerName} for instance {instance.InstanceName}. No data was found for this instance, so skipping.", target: instance, continueLoop: true);
                    continue;
                }

                PSObject tcpIpProperties = BuildTcpIpProperties(wmiSpTcp!);
                object[] tcpIpAddresses = BuildTcpIpAddresses(wmiSpTcp!);

                // Gather service account, regRoot, vsname from WMI service AdvancedProperties
                string? serviceAccount = null, regRoot = null, vsname = null;
                foreach (Service svc in wmi.Services)
                {
                    if (string.Equals(svc.DisplayName, $"SQL Server ({instance.InstanceName})", StringComparison.OrdinalIgnoreCase))
                    {
                        serviceAccount = svc.ServiceAccount;
                        // AdvancedProperty is internal in the SQL WMI assembly — use dynamic
                        foreach (dynamic ap in svc.AdvancedProperties)
                        {
                            if (ap.Name == "REGROOT") regRoot = ap.Value?.ToString();
                            else if (ap.Name == "VSNAME") vsname = ap.Value?.ToString();
                        }
                        WriteMessage(MessageLevel.Verbose, $"regRoot = '{regRoot}' / vsname = '{vsname}'");

                        if (string.IsNullOrEmpty(regRoot))
                        {
                            // Fallback: match AdvancedProperty.ToString() which includes Name and Value
                            foreach (dynamic ap in svc.AdvancedProperties)
                            {
                                string apStr = ap.ToString() ?? string.Empty;
                                if (apStr.IndexOf("REGROOT", StringComparison.Ordinal) >= 0)
                                {
                                    string[] parts = apStr.Split(new[] { "Value=" }, StringSplitOptions.None);
                                    if (parts.Length > 1) regRoot = parts[1];
                                }
                                else if (apStr.IndexOf("VSNAME", StringComparison.Ordinal) >= 0)
                                {
                                    string[] parts = apStr.Split(new[] { "Value=" }, StringSplitOptions.None);
                                    if (parts.Length > 1) vsname = parts[1];
                                }
                            }
                            WriteMessage(MessageLevel.Verbose, $"regRoot = '{regRoot}' / vsname = '{vsname}'");
                            if (!string.IsNullOrEmpty(regRoot))
                                WriteMessage(MessageLevel.Verbose, $"regRoot = '{regRoot}' / vsname = '{vsname}'");
                            else
                                WriteMessage(MessageLevel.Verbose, "Can't find regRoot");
                        }
                        break;
                    }
                }

                // Read registry data via StdRegProv WMI (avoids dependency on Remote Registry service)
                object outputCertificate;
                object outputAdvanced;
                PSObject[]? suitableCertificate = null;

                if (!string.IsNullOrEmpty(regRoot))
                {
                    string superSocketPath = $@"{regRoot}\MSSQLServer\SuperSocketNetLib";
                    try
                    {
                        using ManagementClass stdReg = GetStdRegProv(computerName);
                        string? acceptedSPNs = GetRegStringValue(stdReg, superSocketPath, "AcceptedSPNs");
                        string? thumbprint = GetRegStringValue(stdReg, superSocketPath, "Certificate");
                        bool extendedProtection = ConvertDWordToBool(GetRegDWordValue(stdReg, superSocketPath, "ExtendedProtection"));
                        bool forceEncryption = ConvertDWordToBool(GetRegDWordValue(stdReg, superSocketPath, "ForceEncryption"));
                        bool hideInstance = ConvertDWordToBool(GetRegDWordValue(stdReg, superSocketPath, "HideInstance"));

                        X509Certificate2? cert = null;
                        if (!string.IsNullOrEmpty(thumbprint))
                            cert = FindCertByThumbprintInRegistry(stdReg, thumbprint!);

                        PSObject certObj = new();
                        certObj.Properties.Add(new PSNoteProperty("VSName", vsname));
                        certObj.Properties.Add(new PSNoteProperty("ServiceAccount", serviceAccount));
                        certObj.Properties.Add(new PSNoteProperty("ForceEncryption", forceEncryption));
                        certObj.Properties.Add(new PSNoteProperty("FriendlyName", cert?.FriendlyName));
                        certObj.Properties.Add(new PSNoteProperty("DnsNameList", cert is null ? null : (object)GetDnsNamesArray(cert)));
                        certObj.Properties.Add(new PSNoteProperty("Thumbprint", cert?.Thumbprint));
                        certObj.Properties.Add(new PSNoteProperty("Generated", (object?)cert?.NotBefore));
                        certObj.Properties.Add(new PSNoteProperty("Expires", (object?)cert?.NotAfter));
                        certObj.Properties.Add(new PSNoteProperty("IssuedTo", cert?.Subject));
                        certObj.Properties.Add(new PSNoteProperty("IssuedBy", cert?.Issuer));
                        certObj.Properties.Add(new PSNoteProperty("Certificate", cert));
                        outputCertificate = certObj;

                        PSObject advObj = new();
                        advObj.Properties.Add(new PSNoteProperty("ForceEncryption", forceEncryption));
                        advObj.Properties.Add(new PSNoteProperty("HideInstance", hideInstance));
                        advObj.Properties.Add(new PSNoteProperty("AcceptedSPNs", acceptedSPNs));
                        advObj.Properties.Add(new PSNoteProperty("ExtendedProtection", extendedProtection));
                        outputAdvanced = advObj;

                        string networkName = !string.IsNullOrEmpty(vsname) ? vsname! : computerName;
                        suitableCertificate = GetSuitableCertificates(stdReg, networkName);
                    }
                    catch (Exception ex)
                    {
                        // PS: $outputCertificate = $outputAdvanced = "Failed to get information from registry: $_"
                        outputCertificate = outputAdvanced = $"Failed to get information from registry: {ex}";
                    }
                }
                else
                {
                    outputCertificate = outputAdvanced = "Failed to get information from registry: Path not found";
                }

                // Emit output based on OutputType
                EmitOutput(instance, wmiSpSm!, wmiSpNp!, wmiSpTcp!, tcpIpProperties, tcpIpAddresses,
                    outputCertificate, suitableCertificate, outputAdvanced, vsname, serviceAccount);
            }
            catch (Exception ex)
            {
                StopFunction($"Failed to collect network configuration from {instance.ComputerName} for instance {instance.InstanceName}.", target: instance, exception: ex, continueLoop: true);
                continue;
            }
        }
    }

    private void EmitOutput(
        DbaInstanceParameter instance,
        ServerProtocol wmiSpSm, ServerProtocol wmiSpNp, ServerProtocol wmiSpTcp,
        PSObject tcpIpProperties, object[] tcpIpAddresses,
        object outputCertificate, PSObject[]? suitableCertificate, object outputAdvanced,
        string? vsname, string? serviceAccount)
    {
        string sqlInstance = instance.SqlFullName.TrimStart('[').TrimEnd(']');

        if (OutputType == "Full")
        {
            PSObject result = new();
            result.Properties.Add(new PSNoteProperty("ComputerName", instance.ComputerName));
            result.Properties.Add(new PSNoteProperty("InstanceName", instance.InstanceName));
            result.Properties.Add(new PSNoteProperty("SqlInstance", sqlInstance));
            result.Properties.Add(new PSNoteProperty("SharedMemoryEnabled", wmiSpSm.IsEnabled));
            result.Properties.Add(new PSNoteProperty("NamedPipesEnabled", wmiSpNp.IsEnabled));
            result.Properties.Add(new PSNoteProperty("TcpIpEnabled", wmiSpTcp.IsEnabled));
            result.Properties.Add(new PSNoteProperty("TcpIpProperties", tcpIpProperties));
            result.Properties.Add(new PSNoteProperty("TcpIpAddresses", tcpIpAddresses));
            result.Properties.Add(new PSNoteProperty("Certificate", outputCertificate));
            result.Properties.Add(new PSNoteProperty("SuitableCertificate", suitableCertificate));
            result.Properties.Add(new PSNoteProperty("Advanced", outputAdvanced));
            WriteObject(result);
        }
        else if (OutputType == "ServerProtocols")
        {
            PSObject result = new();
            result.Properties.Add(new PSNoteProperty("ComputerName", instance.ComputerName));
            result.Properties.Add(new PSNoteProperty("InstanceName", instance.InstanceName));
            result.Properties.Add(new PSNoteProperty("SqlInstance", sqlInstance));
            result.Properties.Add(new PSNoteProperty("SharedMemoryEnabled", wmiSpSm.IsEnabled));
            result.Properties.Add(new PSNoteProperty("NamedPipesEnabled", wmiSpNp.IsEnabled));
            result.Properties.Add(new PSNoteProperty("TcpIpEnabled", wmiSpTcp.IsEnabled));
            WriteObject(result);
        }
        else if (OutputType == "TcpIpProperties")
        {
            PSObject result = new();
            result.Properties.Add(new PSNoteProperty("ComputerName", instance.ComputerName));
            result.Properties.Add(new PSNoteProperty("InstanceName", instance.InstanceName));
            result.Properties.Add(new PSNoteProperty("SqlInstance", sqlInstance));
            result.Properties.Add(new PSNoteProperty("Enabled", tcpIpProperties.Properties["Enabled"]?.Value));
            result.Properties.Add(new PSNoteProperty("KeepAlive", tcpIpProperties.Properties["KeepAlive"]?.Value));
            result.Properties.Add(new PSNoteProperty("ListenAll", tcpIpProperties.Properties["ListenAll"]?.Value));
            WriteObject(result);
        }
        else if (OutputType == "TcpIpAddresses")
        {
            bool listenAll = tcpIpProperties.Properties["ListenAll"]?.Value as bool? ?? false;
            if (listenAll)
            {
                // Return only IPAll
                foreach (object addr in tcpIpAddresses)
                {
                    PSObject ip = (PSObject)addr;
                    if (string.Equals(ip.Properties["Name"]?.Value?.ToString(), "IPAll", StringComparison.OrdinalIgnoreCase))
                    {
                        PSObject result = new();
                        result.Properties.Add(new PSNoteProperty("ComputerName", instance.ComputerName));
                        result.Properties.Add(new PSNoteProperty("InstanceName", instance.InstanceName));
                        result.Properties.Add(new PSNoteProperty("SqlInstance", sqlInstance));
                        result.Properties.Add(new PSNoteProperty("Name", ip.Properties["Name"]?.Value));
                        result.Properties.Add(new PSNoteProperty("TcpDynamicPorts", ip.Properties["TcpDynamicPorts"]?.Value));
                        result.Properties.Add(new PSNoteProperty("TcpPort", ip.Properties["TcpPort"]?.Value));
                        WriteObject(result);
                        break;
                    }
                }
            }
            else
            {
                foreach (object addr in tcpIpAddresses)
                {
                    PSObject ip = (PSObject)addr;
                    if (string.Equals(ip.Properties["Name"]?.Value?.ToString(), "IPAll", StringComparison.OrdinalIgnoreCase))
                        continue;
                    PSObject result = new();
                    result.Properties.Add(new PSNoteProperty("ComputerName", instance.ComputerName));
                    result.Properties.Add(new PSNoteProperty("InstanceName", instance.InstanceName));
                    result.Properties.Add(new PSNoteProperty("SqlInstance", sqlInstance));
                    result.Properties.Add(new PSNoteProperty("Name", ip.Properties["Name"]?.Value));
                    result.Properties.Add(new PSNoteProperty("Active", ip.Properties["Active"]?.Value));
                    result.Properties.Add(new PSNoteProperty("Enabled", ip.Properties["Enabled"]?.Value));
                    result.Properties.Add(new PSNoteProperty("IpAddress", ip.Properties["IpAddress"]?.Value));
                    result.Properties.Add(new PSNoteProperty("TcpDynamicPorts", ip.Properties["TcpDynamicPorts"]?.Value));
                    result.Properties.Add(new PSNoteProperty("TcpPort", ip.Properties["TcpPort"]?.Value));
                    WriteObject(result);
                }
            }
        }
        else if (OutputType == "Certificate")
        {
            // PS: if ($netConf.Certificate -like 'Failed*')
            if (outputCertificate is string certStr && certStr.StartsWith("Failed", StringComparison.Ordinal))
            {
                StopFunction($"Failed to collect certificate information from {instance.ComputerName} for instance {instance.InstanceName}: {certStr}", target: instance, continueLoop: true);
                return;
            }
            PSObject? certData = outputCertificate as PSObject;
            PSObject result = new();
            result.Properties.Add(new PSNoteProperty("ComputerName", instance.ComputerName));
            result.Properties.Add(new PSNoteProperty("InstanceName", instance.InstanceName));
            result.Properties.Add(new PSNoteProperty("SqlInstance", sqlInstance));
            result.Properties.Add(new PSNoteProperty("VSName", certData?.Properties["VSName"]?.Value));
            result.Properties.Add(new PSNoteProperty("ServiceAccount", certData?.Properties["ServiceAccount"]?.Value));
            result.Properties.Add(new PSNoteProperty("ForceEncryption", certData?.Properties["ForceEncryption"]?.Value));
            result.Properties.Add(new PSNoteProperty("FriendlyName", certData?.Properties["FriendlyName"]?.Value));
            result.Properties.Add(new PSNoteProperty("DnsNameList", certData?.Properties["DnsNameList"]?.Value));
            result.Properties.Add(new PSNoteProperty("Thumbprint", certData?.Properties["Thumbprint"]?.Value));
            result.Properties.Add(new PSNoteProperty("Generated", certData?.Properties["Generated"]?.Value));
            result.Properties.Add(new PSNoteProperty("Expires", certData?.Properties["Expires"]?.Value));
            result.Properties.Add(new PSNoteProperty("IssuedTo", certData?.Properties["IssuedTo"]?.Value));
            result.Properties.Add(new PSNoteProperty("IssuedBy", certData?.Properties["IssuedBy"]?.Value));
            result.Properties.Add(new PSNoteProperty("Certificate", certData?.Properties["Certificate"]?.Value));

            // PS: Select-DefaultView -Property $defaultView (excludes VSName if empty)
            List<string> defaultView = new() { "ComputerName", "InstanceName", "SqlInstance", "VSName", "ServiceAccount", "ForceEncryption", "FriendlyName", "DnsNameList", "Thumbprint", "Generated", "Expires", "IssuedTo", "IssuedBy" };
            object? vsnameVal = certData?.Properties["VSName"]?.Value;
            if (vsnameVal is null || string.IsNullOrEmpty(vsnameVal.ToString()))
            {
                // PS: $defaultView | Where-Object { $_ -ne 'VSNAME' }  (-ne is case-insensitive)
                defaultView.RemoveAll(s => string.Equals(s, "VSName", StringComparison.OrdinalIgnoreCase));
            }
            Dataplat.Dbatools.Utility.OutputHelper.SetDefaultDisplayPropertySet(result, defaultView.ToArray());
            WriteObject(result);
        }
    }
}
