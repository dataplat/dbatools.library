#nullable enable

using System;
using System.Collections.Generic;
using System.Management.Automation;
using System.Text.RegularExpressions;
using Dataplat.Dbatools.Parameter;
using Microsoft.SqlServer.Management.Smo.Wmi;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves SQL Server startup parameters from the Windows service configuration.
/// Port of public/Get-DbaStartupParameter.ps1; surface pinned by migration/baselines/Get-DbaStartupParameter.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaStartupParameter")]
[OutputType(typeof(PSObject))]
public sealed class GetDbaStartupParameterCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Allows you to login to servers using alternate Windows credentials.</summary>
    [Parameter(Position = 1)]
    [Alias("SqlCredential")]
    public PSCredential? Credential { get; set; }

    /// <summary>Returns only essential startup information: file paths, trace flags, and the parameter string.</summary>
    [Parameter]
    public SwitchParameter Simple { get; set; }

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
                string computerName = instance.ComputerName;
                string instanceName = instance.InstanceName;
                string ogInstance = instance.FullSmoName;

                string serviceDisplayName = $"SQL Server ({instanceName})";

                // This command is in the internal function
                // It's sorta like Invoke-Command.
                ManagedComputer wmi;
                if (Credential is not null)
                {
                    // ManagedComputer requires a plain-text password; this is a necessary
                    // exception — the WMI API has no secure-string overload.
                    System.Net.NetworkCredential netCred = Credential.GetNetworkCredential();
                    string fullUserName = string.IsNullOrEmpty(netCred.Domain)
                        ? netCred.UserName
                        : $"{netCred.Domain}\\{netCred.UserName}";
                    wmi = new ManagedComputer(computerName, fullUserName, netCred.Password);
                }
                else
                {
                    wmi = new ManagedComputer(computerName);
                }

                List<Service> matchingServices = new();
                foreach (Service svc in wmi.Services)
                {
                    if (string.Equals(svc.DisplayName, serviceDisplayName, StringComparison.OrdinalIgnoreCase))
                    {
                        matchingServices.Add(svc);
                    }
                }

                if (matchingServices.Count == 0)
                {
                    throw new Exception($"SQL Server service '{serviceDisplayName}' was not found on {computerName}.");
                }

                if (matchingServices.Count > 1)
                {
                    throw new Exception($"Multiple SQL Server services named '{serviceDisplayName}' were found on {computerName}.");
                }

                Service wmisvc = matchingServices[0];

                string[] startupParams = wmisvc.StartupParameters.Split(';');

                string? masterData = null, masterLog = null, errorLog = null;
                List<string> traceFlagRaw = new(), debugFlagRaw = new();

                foreach (string p in startupParams)
                {
                    if (p.StartsWith("-d", StringComparison.Ordinal))
                        masterData = p;
                    else if (p.StartsWith("-l", StringComparison.Ordinal))
                        masterLog = p;
                    else if (p.StartsWith("-e", StringComparison.Ordinal))
                        errorLog = p;
                    else if (p.StartsWith("-T", StringComparison.Ordinal))
                        traceFlagRaw.Add(p);
                    else if (p.StartsWith("-t", StringComparison.Ordinal))
                        debugFlagRaw.Add(p);
                }

                // if ($traceFlags.length -eq 0) { $traceFlags = "None" } else { $traceFlags = [int[]]$traceFlags.substring(2) }
                object traceFlags = StartupParameterParser.FlagsOrNone(traceFlagRaw);

                // if ($debugFlags.length -eq 0) { $debugFlags = "None" } else { $debugFlags = [int[]]$debugFlags.substring(2) }
                object debugFlags = StartupParameterParser.FlagsOrNone(debugFlagRaw);

                PSObject result = new();
                result.Properties.Add(new PSNoteProperty("ComputerName", computerName));
                result.Properties.Add(new PSNoteProperty("InstanceName", instanceName));
                result.Properties.Add(new PSNoteProperty("SqlInstance", ogInstance));

                if (Simple.IsPresent)
                {
                    // Simple mode returns 9 essential properties (per .OUTPUTS docs).
                    result.Properties.Add(new PSNoteProperty("MasterData", masterData?.TrimStart('-', 'd')));
                    result.Properties.Add(new PSNoteProperty("MasterLog", masterLog?.TrimStart('-', 'l')));
                    result.Properties.Add(new PSNoteProperty("ErrorLog", errorLog?.TrimStart('-', 'e')));
                    result.Properties.Add(new PSNoteProperty("TraceFlags", traceFlags));
                    result.Properties.Add(new PSNoteProperty("DebugFlags", debugFlags));
                    result.Properties.Add(new PSNoteProperty("ParameterString", wmisvc.StartupParameters));
                }
                else
                {
                    // From https://msdn.microsoft.com/en-us/library/ms190737.aspx

                    string? commandPromptParm = null, minimalStartParm = null;
                    string? memoryToReserveParm = null, noEventLogsParm = null;
                    string? instanceStartParm = null, disableMonitoringParm = null, increasedExtentsParm = null;
                    string? singleUserParm = null;

                    foreach (string p in startupParams)
                    {
                        if (p == "-c") commandPromptParm = p;
                        else if (p == "-f") minimalStartParm = p;
                        else if (p.StartsWith("-g", StringComparison.Ordinal)) memoryToReserveParm = p;
                        else if (p == "-n") noEventLogsParm = p;
                        else if (p == "-s") instanceStartParm = p;
                        else if (p == "-x") disableMonitoringParm = p;
                        else if (p == "-E") increasedExtentsParm = p;  // case-sensitive: $_ -ceq '-E'
                        else if (p.StartsWith("-m", StringComparison.Ordinal)) singleUserParm = p;
                    }

                    bool minimalStart = false, noEventLogs = false, instanceStart = false;
                    bool disableMonitoring = false, increasedExtents = false, commandPrompt = false, singleUser = false;

                    if (commandPromptParm is not null) commandPrompt = true;
                    if (minimalStartParm is not null) minimalStart = true;
                    // if ($null -eq $memoryToReserve) { $memoryToReserve = 0 }
                    object memoryToReserve = memoryToReserveParm is null ? (object)0 : memoryToReserveParm;
                    if (noEventLogsParm is not null) noEventLogs = true;
                    if (instanceStartParm is not null) instanceStart = true;
                    if (disableMonitoringParm is not null) disableMonitoring = true;
                    if (increasedExtentsParm is not null) increasedExtents = true;

                    string? singleUserDetails = null;
                    if (singleUserParm is not null && singleUserParm.Length != 0)
                    {
                        singleUser = true;
                        singleUserDetails = singleUserParm.TrimStart('-', 'm');
                    }

                    result.Properties.Add(new PSNoteProperty("MasterData", Regex.Replace(masterData ?? string.Empty, @"^-[dD]", string.Empty)));
                    result.Properties.Add(new PSNoteProperty("MasterLog", Regex.Replace(masterLog ?? string.Empty, @"^-[lL]", string.Empty)));
                    result.Properties.Add(new PSNoteProperty("ErrorLog", Regex.Replace(errorLog ?? string.Empty, @"^-[eE]", string.Empty)));
                    result.Properties.Add(new PSNoteProperty("TraceFlags", traceFlags));
                    result.Properties.Add(new PSNoteProperty("DebugFlags", debugFlags));
                    result.Properties.Add(new PSNoteProperty("CommandPromptStart", commandPrompt));
                    result.Properties.Add(new PSNoteProperty("MinimalStart", minimalStart));
                    result.Properties.Add(new PSNoteProperty("MemoryToReserve", memoryToReserve));
                    result.Properties.Add(new PSNoteProperty("SingleUser", singleUser));
                    result.Properties.Add(new PSNoteProperty("SingleUserName", singleUserDetails));
                    result.Properties.Add(new PSNoteProperty("NoLoggingToWinEvents", noEventLogs));
                    result.Properties.Add(new PSNoteProperty("StartAsNamedInstance", instanceStart));
                    result.Properties.Add(new PSNoteProperty("DisableMonitoring", disableMonitoring));
                    result.Properties.Add(new PSNoteProperty("IncreasedExtents", increasedExtents));
                    result.Properties.Add(new PSNoteProperty("ParameterString", wmisvc.StartupParameters));
                }

                WriteObject(result);
            }
            catch (Exception ex)
            {
                StopFunction($"{instance} failed.", target: instance, exception: ex, continueLoop: true);
                continue;
            }
        }
    }

}
