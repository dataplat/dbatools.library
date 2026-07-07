#nullable enable
#pragma warning disable CA1416 // Windows-only: WMI service discovery helpers

using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Management.Automation;
using Dataplat.Dbatools.Message;

namespace Dataplat.Dbatools.Commands;

public sealed partial class GetDbaServiceCommand
{
    // Verify WMI connectivity by connecting to root\Microsoft.
    // Returns the resolved computer name (the input name as-is; DNS resolution is left to WMI).
    private string VerifyConnectivity(string computer)
    {
        var scope = new ManagementScope($@"\\{computer}\root\Microsoft", _wmiOptions);
        scope.Connect();
        using var searcher = new ManagementObjectSearcher(scope,
            new ObjectQuery("Select Name FROM __NAMESPACE"),
            new EnumerationOptions { ReturnImmediately = true });
        foreach (ManagementObject _ in searcher.Get()) { break; }
        return computer;
    }

    // Find ComputerManagement* namespaces under root\Microsoft\SQLServer, sorted descending (highest first).
    private List<string> GetComputerManagementNamespaces(string resolvedComputer, string computer)
    {
        var result = new List<string>();
        try
        {
            var scope = new ManagementScope($@"\\{resolvedComputer}\root\Microsoft\SQLServer", _wmiOptions);
            scope.Connect();
            using var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery("Select Name FROM __NAMESPACE WHERE Name Like 'ComputerManagement%'"));
            foreach (ManagementObject obj in searcher.Get())
                if (obj["Name"] is string n) result.Add(n);
            result.Sort((a, b) => string.Compare(b, a, StringComparison.Ordinal));
            WriteMessage(MessageLevel.Verbose, $"The following namespaces have been found: {string.Join(", ", result)}.");
        }
        catch
        {
            WriteMessage(MessageLevel.Verbose, $"No namespaces found in relevant namespace on {computer}.");
        }
        return result;
    }

    //Add other properties and methods (script methods match the PS source verbatim)
    private static void AddScriptMethods(PSObject psObj)
    {
        psObj.Methods.Add(new PSScriptMethod("Stop",
            ScriptBlock.Create("param ([bool]$Force = $false)\r\nStop-DbaService -InputObject $this -Force:$Force")));
        psObj.Methods.Add(new PSScriptMethod("Start",
            ScriptBlock.Create("Start-DbaService -InputObject $this")));
        psObj.Methods.Add(new PSScriptMethod("Restart",
            ScriptBlock.Create("param ([bool]$Force = $false)\r\nRestart-DbaService -InputObject $this -Force:$Force")));
        psObj.Methods.Add(new PSScriptMethod("ChangeStartMode",
            ScriptBlock.Create(
                "param ([parameter(Mandatory)][string]$Mode)\r\n" +
                "$supportedModes = @(\"Automatic\", \"Manual\", \"Disabled\")\r\n" +
                "if ($Mode -notin $supportedModes) {\r\n" +
                "    Stop-Function -Message (\"Incorrect mode '$Mode'. Use one of the following values: {0}\" -f ($supportedModes -join ' | ')) -EnableException $false -FunctionName 'Get-DbaService'\r\n" +
                "    Return\r\n" +
                "}\r\n" +
                "Set-ServiceStartMode -InputObject $this -Mode $Mode -ErrorAction Stop\r\n" +
                "$this.StartMode = $Mode")));
    }

    private void AddAdvancedProperties(PSObject psObj, string serviceName, string computer, string ns, int sqlType)
    {
        List<ManagementObject> advProps = new();
        try
        {
            var scope = new ManagementScope($@"\\{computer}\root\Microsoft\SQLServer\{ns}", _wmiOptions);
            scope.Connect();
            using var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery($"SELECT * FROM SqlServiceAdvancedProperty WHERE ServiceName = '{serviceName}'"));
            advProps = searcher.Get().Cast<ManagementObject>().ToList();
        }
        catch { /* best-effort */ }

        string? GetStr(string prop) =>
            advProps.FirstOrDefault(p => string.Equals(p["PropertyName"] as string, prop, StringComparison.OrdinalIgnoreCase))
                    ?["PropertyStrValue"] as string;
        object? GetNum(string prop) =>
            advProps.FirstOrDefault(p => string.Equals(p["PropertyName"] as string, prop, StringComparison.OrdinalIgnoreCase))
                    ?["PropertyNumValue"];

        psObj.Properties.Add(new PSNoteProperty("Version", GetStr("VERSION")));
        psObj.Properties.Add(new PSNoteProperty("SPLevel", GetNum("SPLEVEL")));
        psObj.Properties.Add(new PSNoteProperty("SkuName", GetStr("SKUNAME")));

        // $ClusterServiceTypeList = @(1, 2, 5, 7)
        if (sqlType == 1 || sqlType == 2 || sqlType == 5 || sqlType == 7)
        {
            psObj.Properties.Add(new PSNoteProperty("Clustered", GetNum("CLUSTERED")));
            psObj.Properties.Add(new PSNoteProperty("VSName",    GetStr("VSNAME")));
        }
        else
        {
            psObj.Properties.Add(new PSNoteProperty("Clustered", string.Empty));
            psObj.Properties.Add(new PSNoteProperty("VSName",    string.Empty));
        }

        // if ($service.SQLServiceType -eq 1) — Engine services get the SqlInstance property
        if (sqlType == 1)
        {
            string? vsName   = psObj.Properties["VSName"]?.Value as string;
            string? instName = psObj.Properties["InstanceName"]?.Value as string;
            string? compName = psObj.Properties["ComputerName"]?.Value as string;
            string sqlInst   = !string.IsNullOrEmpty(vsName) ? vsName! : (compName ?? computer);
            if (!string.Equals(instName, "MSSQLSERVER", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(instName))
                sqlInst += "\\" + instName;
            psObj.Properties.Add(new PSNoteProperty("SqlInstance", sqlInst));
        }
        else
        {
            psObj.Properties.Add(new PSNoteProperty("SqlInstance", string.Empty));
        }
    }

    private ConnectionOptions BuildConnectionOptions()
    {
        var opts = new ConnectionOptions();
        if (Credential != null)
        {
            // WMI API has no SecureString overload; plain-text password is a necessary exception.
            System.Net.NetworkCredential netCred = Credential.GetNetworkCredential();
            opts.Username = string.IsNullOrEmpty(netCred.Domain)
                ? netCred.UserName
                : $"{netCred.Domain}\\{netCred.UserName}";
            opts.Password = netCred.Password;
        }
        return opts;
    }

    internal static string MapServiceType(int sqlServiceType)
    {
        foreach (var (name, ids) in ServiceIdMap)
            if (Array.IndexOf(ids, sqlServiceType) >= 0)
                return name;
        return "Unknown";
    }

    private static string MapState(int state) => state switch
    {
        1 => "Stopped",
        2 => "Start Pending",
        3 => "Stop Pending",
        4 => "Running",
        _ => "Unknown"
    };

    private static string MapStartMode(int mode) => mode switch
    {
        1 => "Unknown",
        2 => "Automatic",
        3 => "Manual",
        4 => "Disabled",
        _ => "Unknown"
    };

    private static string DeriveInstanceName(string serviceName, string serviceType)
    {
        if (DefaultInstanceServiceNames.Contains(serviceName))
            return "MSSQLSERVER";
        if (InstancedServiceTypes.Contains(serviceType))
        {
            int dollarIdx = serviceName.IndexOf('$');
            return dollarIdx >= 0 ? serviceName.Substring(dollarIdx + 1) : "Unknown";
        }
        return string.Empty;
    }

    // Port of private/functions/Get-DbaReportingService.ps1
    // Queries the SSRS WMI provider for SQL Server Reporting Services and PowerBI Report Server instances.
    private List<PSObject> GetReportingServices(string computer, string[]? instanceFilter, string[]? serviceNameFilter)
    {
        var result = new List<PSObject>();
        var serviceArray = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        //If filtering by service name create a dynamic WHERE clause
        string searchClause = string.Empty;
        if (serviceNameFilter != null && serviceNameFilter.Length > 0)
            searchClause = $" WHERE ServiceName = '{string.Join("' OR ServiceName = '", serviceNameFilter)}'";

        IEnumerable<ManagementObject> namespaces;
        try
        {
            WriteMessage(MessageLevel.VeryVerbose, $"Getting Reporting Server namespace on {computer}", target: computer);
            var scope = new ManagementScope($@"\\{computer}\root\Microsoft\SQLServer\ReportServer", _wmiOptions);
            scope.Connect();
            using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("Select Name FROM __NAMESPACE"));
            namespaces = searcher.Get().Cast<ManagementObject>().ToList();
        }
        catch
        {
            // No SQLServer\ReportServer Namespace on $Computer. Please note that this function is available from SQL 2005 up.
            WriteMessage(MessageLevel.Verbose, $"No SQLServer\\ReportServer Namespace on {computer}. Please note that this function is available from SQL 2005 up.");
            return result;
        }

        // querying SqlService namespace
        foreach (ManagementObject nsObj in namespaces)
        {
            string? nsName = nsObj["Name"] as string;
            if (nsName == null) continue;

            WriteMessage(MessageLevel.Verbose, $"Getting version from the namespace {nsName} on {computer}.", target: computer);

            ManagementObject? versionObj;
            try
            {
                var vScope = new ManagementScope($@"\\{computer}\root\Microsoft\SQLServer\ReportServer\{nsName}", _wmiOptions);
                vScope.Connect();
                using var vSearcher = new ManagementObjectSearcher(vScope, new ObjectQuery("SELECT Name FROM __NAMESPACE"));
                versionObj = vSearcher.Get().Cast<ManagementObject>().FirstOrDefault();
            }
            catch (Exception ex)
            {
                // No version Namespace on $Computer. Please note that this function is available from SQL 2005 up.
                StopFunction($"No version Namespace on {computer}. Please note that this function is available from SQL 2005 up.", exception: ex, continueLoop: true);
                continue;
            }
            if (versionObj == null) continue;
            string? versionName = versionObj["Name"] as string;
            if (versionName == null) continue;

            IEnumerable<ManagementObject> services;
            try
            {
                var sScope = new ManagementScope(
                    $@"\\{computer}\root\Microsoft\SQLServer\ReportServer\{nsName}\{versionName}\Admin", _wmiOptions);
                sScope.Connect();
                string cimQuery = "SELECT * FROM MSReportServer_ConfigurationSetting" + searchClause;
                using var sSearcher = new ManagementObjectSearcher(sScope, new ObjectQuery(cimQuery));
                services = sSearcher.Get().Cast<ManagementObject>().ToList();
            }
            catch (Exception ex)
            {
                StopFunction($"Failed to acquire services from namespace {nsName}\\{versionName}.", target: computer, exception: ex, continueLoop: true);
                continue;
            }

            foreach (ManagementObject svc in services)
            {
                string? svcServiceName = svc["ServiceName"] as string;
                if (svcServiceName == null || serviceArray.Contains(svcServiceName)) continue;

                string? svcInstanceName = svc["InstanceName"] as string;
                if (instanceFilter != null && instanceFilter.Length > 0 &&
                    !instanceFilter.Contains(svcInstanceName ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                    continue;

                string reportServiceType = string.Equals(nsName, "RS_PBIRS", StringComparison.OrdinalIgnoreCase) ? "PowerBI" : "SSRS";

                // Query Win32_Service for current state, startup mode, and display name
                string svcState = string.Empty, svcStartMode = string.Empty, svcDisplayName = string.Empty, svcComputerName = computer;
                try
                {
                    var cimScope = new ManagementScope($@"\\{computer}\root\cimv2", _wmiOptions);
                    cimScope.Connect();
                    using var cimSearcher = new ManagementObjectSearcher(cimScope,
                        new ObjectQuery($"SELECT * FROM Win32_Service WHERE Name = '{svcServiceName}'"));
                    ManagementObject? svc32 = cimSearcher.Get().Cast<ManagementObject>().FirstOrDefault();
                    if (svc32 != null)
                    {
                        svcComputerName = svc32["SystemName"] as string ?? computer;
                        svcState        = svc32["State"]     as string ?? string.Empty;
                        svcDisplayName  = svc32["DisplayName"] as string ?? string.Empty;
                        string? sm      = svc32["StartMode"] as string;
                        // Auto { 'Automatic' } — Replacing for consistency to match other SQL Services
                        svcStartMode    = string.Equals(sm, "Auto", StringComparison.OrdinalIgnoreCase) ? "Automatic" : (sm ?? string.Empty);
                    }
                }
                catch { /* best-effort */ }

                string? startName = svc["WindowsServiceIdentityActual"] as string;

                var psObj = new PSObject();
                psObj.Properties.Add(new PSNoteProperty("ComputerName",    svcComputerName));
                psObj.Properties.Add(new PSNoteProperty("ServiceName",     svcServiceName));
                psObj.Properties.Add(new PSNoteProperty("ServiceType",     reportServiceType));
                psObj.Properties.Add(new PSNoteProperty("InstanceName",    svcInstanceName ?? string.Empty));
                psObj.Properties.Add(new PSNoteProperty("DisplayName",     svcDisplayName));
                psObj.Properties.Add(new PSNoteProperty("StartName",       startName ?? string.Empty));
                psObj.Properties.Add(new PSNoteProperty("State",           svcState));
                psObj.Properties.Add(new PSNoteProperty("StartMode",       svcStartMode));
                psObj.Properties.Add(new PSNoteProperty("ServicePriority", 100));
                AddScriptMethods(psObj);

                serviceArray.Add(svcServiceName);
                result.Add(psObj);
            }
        }

        return result;
    }
}
