#nullable enable
#pragma warning disable CA1416 // Windows-only: SQL WMI ManagedComputer, WindowsPrincipal

using System;
using System.Security.Principal;
using Dataplat.Dbatools.Configuration;
using Dataplat.Dbatools.Connection;
using Dataplat.Dbatools.Parameter;
using Microsoft.SqlServer.Management.Smo.Wmi;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Shared REGROOT/VSNAME resolution and elevation gating for the Force Network Encryption
/// command family (Get/Enable/Disable-DbaForceNetworkEncryption). Extracted verbatim from
/// GetDbaForceNetworkEncryptionCommand (W5-011) so the three commands cannot drift.
/// </summary>
internal static class ForceEncryptionResolver
{
    internal sealed class Resolution
    {
        public string RegRoot = "";
        public string? Vsname;
        public string? InstanceName;
        public string? ServiceAccount;
    }

    /// <summary>The failure kinds the callers map to their own Stop-Function messages.</summary>
    internal enum Outcome
    {
        Ok,
        AccessFailed,
        InstanceNotFound
    }

    // PS: $null = Test-ElevationRequirement -ComputerName $instance -Continue.
    internal static bool RequireElevationSatisfied(DbaInstanceParameter instance)
    {
        if (GetConfigTruthy("commands.test-elevationrequirement.disable"))
        {
            return true;
        }
        bool isElevated = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        // Only a localhost target without an elevated console fails; a remote target passes through.
        return !(instance.IsLocalHost && !isElevated);
    }

    // PS: $sqlwmi = new ManagedComputer(full).Services | Where DisplayName -eq "SQL Server (inst)"
    //     then REGROOT/VSNAME direct read + the -match/-Split "Value=" fallback.
    internal static Outcome Resolve(string fullComputerName, DbaInstanceParameter instance, PSCredentialWrap credential, out Resolution resolution, out Exception? accessException)
    {
        resolution = new Resolution();
        accessException = null;

        Service? sqlwmi = null;
        try
        {
            ManagedComputer wmi = BuildManagedComputer(fullComputerName, credential);
            string wantDisplay = $"SQL Server ({instance.InstanceName})";
            foreach (Service svc in wmi.Services)
            {
                if (string.Equals(svc.DisplayName, wantDisplay, StringComparison.OrdinalIgnoreCase))
                {
                    sqlwmi = svc;
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            accessException = ex;
            return Outcome.AccessFailed;
        }

        string? regRoot = null, vsname = null, instanceName = null, serviceAccount = null;
        if (sqlwmi is not null)
        {
            foreach (dynamic ap in sqlwmi.AdvancedProperties)
            {
                if (string.Equals((string)ap.Name, "REGROOT", StringComparison.OrdinalIgnoreCase))
                    regRoot = ap.Value?.ToString();
                else if (string.Equals((string)ap.Name, "VSNAME", StringComparison.OrdinalIgnoreCase))
                    vsname = ap.Value?.ToString();
            }
            try
            {
                instanceName = sqlwmi.DisplayName.Replace("SQL Server (", "").Replace(")", "");
            }
            catch
            {
                // instance name aliased / absent - PS empty-catch
            }
            serviceAccount = sqlwmi.ServiceAccount;
        }

        if (string.IsNullOrEmpty(regRoot))
        {
            string? regRootMatch = null, vsnameMatch = null;
            if (sqlwmi is not null)
            {
                foreach (dynamic ap in sqlwmi.AdvancedProperties)
                {
                    string apStr = ap.ToString() ?? string.Empty;
                    if (apStr.IndexOf("REGROOT", StringComparison.OrdinalIgnoreCase) >= 0)
                        regRootMatch = apStr;
                    if (apStr.IndexOf("VSNAME", StringComparison.OrdinalIgnoreCase) >= 0)
                        vsnameMatch = apStr;
                }
            }
            if (!string.IsNullOrEmpty(regRootMatch))
            {
                regRoot = SplitAfterValue(regRootMatch);
                vsname = SplitAfterValue(vsnameMatch);
            }
            else
            {
                resolution.Vsname = vsname;
                return Outcome.InstanceNotFound;
            }
        }

        resolution.RegRoot = regRoot ?? "";
        resolution.Vsname = vsname;
        resolution.InstanceName = instanceName;
        resolution.ServiceAccount = serviceAccount;
        return Outcome.Ok;
    }

    private static ManagedComputer BuildManagedComputer(string computerName, PSCredentialWrap credential)
    {
        if (credential.HasValue)
        {
            System.Net.NetworkCredential netCred = credential.GetNetworkCredential();
            string user = string.IsNullOrEmpty(netCred.Domain) ? netCred.UserName : $"{netCred.Domain}\\{netCred.UserName}";
            return new ManagedComputer(computerName, user, netCred.Password);
        }
        return new ManagedComputer(computerName);
    }

    private static string? SplitAfterValue(string? value)
    {
        if (value is null)
        {
            return null;
        }
        string[] parts = value.Split(new[] { "Value=" }, StringSplitOptions.None);
        return parts.Length > 1 ? parts[1] : null;
    }

    private static bool GetConfigTruthy(string name)
    {
        if (ConfigurationHost.Configurations.TryGetValue(name, out Config? config) && config != null && config.Value != null)
        {
            try
            {
                return System.Management.Automation.LanguagePrimitives.IsTrue(config.Value);
            }
            catch
            {
                // malformed config falls back to false
            }
        }
        return false;
    }
}

/// <summary>Tiny wrapper so the resolver stays free of a direct PSCredential null-check idiom.</summary>
internal readonly struct PSCredentialWrap
{
    private readonly System.Management.Automation.PSCredential? _credential;
    internal PSCredentialWrap(System.Management.Automation.PSCredential? credential) { _credential = credential; }
    internal bool HasValue => _credential is not null;
    internal System.Net.NetworkCredential GetNetworkCredential() => _credential!.GetNetworkCredential();
}
