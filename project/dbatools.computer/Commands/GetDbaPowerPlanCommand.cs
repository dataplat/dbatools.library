#nullable enable

using System;
using System.Collections.Generic;
using System.Management.Automation;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Dataplat.Dbatools.Configuration;
using Dataplat.Dbatools.Connection;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;
using Dataplat.Dbatools.Utility;
using Microsoft.Management.Infrastructure;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves Windows power plan configuration from SQL Server hosts.
/// Port of public/Get-DbaPowerPlan.ps1; surface pinned by migration/baselines/Get-DbaPowerPlan.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaPowerPlan")]
[OutputType(typeof(PSObject))]
public sealed class GetDbaPowerPlanCommand : DbaBaseCmdlet
{
    /// <summary>The SQL Server host computer(s) to check for power plan configuration.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[] ComputerName { get; set; } = null!;

    /// <summary>Authenticate to the target computer(s) with alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? Credential { get; set; }

    /// <summary>Returns all available power plans on the target computers instead of just the active plan.</summary>
    [Parameter]
    public SwitchParameter List { get; set; }

    protected override void ProcessRecord()
    {
        if (Interrupted)
        {
            return;
        }

        foreach (DbaInstanceParameter computer in ComputerName)
        {
            // PS: $null = Test-ElevationRequirement -ComputerName $computer -Continue
            // (private/functions/flowcontrol/Test-ElevationRequirement.ps1 inlined: the test
            // only fails for a localhost target in a non-elevated process).
            if (!TestElevationRequirement(computer))
            {
                StopFunction("Console not elevated, but elevation is required to perform some actions on localhost for this command.", continueLoop: true);
                continue;
            }

            // PS: Resolve-DbaNetworkName inlined to the one thing it contributes here:
            // FullComputerName (modules.md 4.3 pattern). Without Invoke-Command2's remote
            // DNS-suffix probe the resolved name stays the input host name, which is the PS
            // source's own fallback when remoting to the target is unavailable; either form
            // reaches the same machine as the CIM target.
            string? computerResolved = ResolveFullComputerName(computer);

            if (string.IsNullOrEmpty(computerResolved))
            {
                StopFunction("Couldn't resolve hostname. Skipping.", continueLoop: true);
                continue;
            }

            WriteMessage(MessageLevel.Verbose, $"Getting Power Plan information from {computer}.");

            List<PSObject> powerPlans;
            try
            {
                // PS: $splatDbaCmObject = @{ ComputerName; EnableException = $true } (+ Credential
                // when bound); EnableException means CIM failures THROW into this catch.
                // Wave 5: the full Get-DbaCmObject protocol chain (the Wmi/PSRemoting-rung
                // deferral of the original port is retired).
                PSCredential? effectiveCredential = TestBound(nameof(Credential)) ? Credential : null;
                CimService.CmObjectRequest planRequest = new()
                {
                    ComputerName = computerResolved!,
                    Credential = effectiveCredential,
                    ClassName = "Win32_PowerPlan",
                    Namespace = @"root\cimv2\power"
                };
                CimService.CmObjectResult planResult = CimService.GetCmObject(planRequest);
                foreach (ErrorRecord passthrough in planResult.PassthroughErrors)
                {
                    WriteError(passthrough);
                }
                powerPlans = planResult.Instances;
            }
            catch (PipelineStoppedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // PS triage: $_.Exception -match "namespace" / "credentials are known to not work".
                // PS stringifies to the exception MESSAGE, so the port matches the message chain
                // only - ToString() would false-match "namespace" in MMI stack-frame signatures.
                string rendered = RenderMessages(ex);
                if (rendered.IndexOf("namespace", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    StopFunction($"Can't get Power Plan Info for {computer}. Unsupported operating system.", target: computer, errorRecord: new ErrorRecord(ex, "dbatools_Get-DbaPowerPlan", ErrorCategory.NotSpecified, computer), continueLoop: true);
                }
                else if (rendered.IndexOf("credentials are known to not work", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    StopFunction($"Can't get Power Plan Info for {computer}. Login failure for {Credential?.UserName}.", target: computer, errorRecord: new ErrorRecord(ex, "dbatools_Get-DbaPowerPlan", ErrorCategory.NotSpecified, computer), continueLoop: true);
                }
                else
                {
                    StopFunction($"Can't get Power Plan Info for {computer}. Check logs for more details.", target: computer, errorRecord: new ErrorRecord(ex, "dbatools_Get-DbaPowerPlan", ErrorCategory.NotSpecified, computer), continueLoop: true);
                }
                continue;
            }

            if (List.ToBool())
            {
                foreach (PSObject powerPlan in powerPlans)
                {
                    string? instanceId = ExtractInstanceGuid(GetInstanceProperty(powerPlan, "InstanceID") as string);
                    PSObject output = new();
                    output.Properties.Add(new PSNoteProperty("ComputerName", computer));
                    output.Properties.Add(new PSNoteProperty("InstanceId", instanceId));
                    output.Properties.Add(new PSNoteProperty("PowerPlan", GetInstanceProperty(powerPlan, "ElementName")));
                    output.Properties.Add(new PSNoteProperty("IsActive", GetInstanceProperty(powerPlan, "IsActive")));
                    output.Properties.Add(new PSNoteProperty("Credential", Credential));
                    OutputHelper.SetDefaultDisplayPropertySet(output, "ComputerName", "PowerPlan", "IsActive");
                    WriteObject(output);
                }
            }
            else
            {
                // PS: $powerPlans | Where-Object IsActive -eq 'True' (bool True survives the
                // string comparison via PS conversion).
                PSObject? activePlan = null;
                foreach (PSObject powerPlan in powerPlans)
                {
                    if (GetInstanceProperty(powerPlan, "IsActive") is bool isActive && isActive)
                    {
                        activePlan = powerPlan;
                        break;
                    }
                }

                string? instanceId = null;
                object? planName = null;
                if (activePlan is not null)
                {
                    instanceId = ExtractInstanceGuid(GetInstanceProperty(activePlan, "InstanceID") as string);
                    planName = GetInstanceProperty(activePlan, "ElementName");
                }
                if (instanceId is null)
                {
                    // PS: if ($null -eq $powerPlan.InstanceID) { $powerPlan.ElementName = "Unknown" }
                    // ACCEPTED DEVIATION: the PS source trips over null before this fallback can
                    // apply (method/property access on $null); the port honors the intended
                    // "Unknown" emission instead of reproducing the null-access errors.
                    planName = "Unknown";
                }

                PSObject output = new();
                output.Properties.Add(new PSNoteProperty("ComputerName", computer));
                output.Properties.Add(new PSNoteProperty("InstanceId", instanceId));
                output.Properties.Add(new PSNoteProperty("PowerPlan", planName));
                output.Properties.Add(new PSNoteProperty("Credential", Credential));
                OutputHelper.SetDefaultDisplayPropertySet(output, "ComputerName", "PowerPlan");
                WriteObject(output);
            }
        }
    }

    private bool TestElevationRequirement(DbaInstanceParameter computer)
    {
        // PS: Get-DbatoolsConfigValue -FullName commands.test-elevationrequirement.disable
        if (GetConfigTruthy("commands.test-elevationrequirement.disable"))
        {
            return true;
        }
        if (!computer.IsLocalHost)
        {
            return true;
        }
        // ACCEPTED DEVIATION: PS on non-Windows faults inside WindowsIdentity.GetCurrent();
        // the port passes the requirement through instead (elevation is meaningless there).
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return true;
        }
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        WindowsPrincipal principal = new(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private string? ResolveFullComputerName(DbaInstanceParameter computer)
    {
        // PS: Resolve-DbaNetworkName honors commands.resolve-dbanetworkname.bypass by
        // echoing the input straight back without any resolution.
        if (GetConfigTruthy("commands.resolve-dbanetworkname.bypass"))
        {
            return computer.ComputerName;
        }

        string cName = computer.IsLocalHost ? Environment.MachineName : computer.ComputerName;
        try
        {
            WriteMessage(MessageLevel.VeryVerbose, $"Resolving {cName} using .NET.Dns GetHostEntry");
            IPHostEntry resolved = Dns.GetHostEntry(cName);
            if (resolved.AddressList is null || resolved.AddressList.Length == 0)
            {
                WriteMessage(MessageLevel.Warning, $"DNS name {cName} not found");
                return null;
            }
        }
        catch (PipelineStoppedException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // PS: Resolve-DbaNetworkName's own Stop-Function -Continue surfaces as a warning
            // only - the caller never forwards EnableException into it, so it never throws.
            WriteMessage(MessageLevel.Warning, $"DNS name {cName} not found", exception: ex);
            return null;
        }
        return cName;
    }

    private static bool GetConfigTruthy(string fullName)
    {
        if (ConfigurationHost.Configurations.TryGetValue(fullName, out Config? config) && config is not null && config.Value is not null)
        {
            return LanguagePrimitives.IsTrue(config.Value);
        }
        return false;
    }

    private static object? GetInstanceProperty(PSObject instance, string name)
    {
        return instance.Properties[name]?.Value;
    }

    private static string RenderMessages(Exception exception)
    {
        List<string> messages = new();
        Exception? current = exception;
        while (current is not null && messages.Count < 10)
        {
            messages.Add(current.Message);
            current = current.InnerException;
        }
        return string.Join(" | ", messages);
    }

    private static string? ExtractInstanceGuid(string? rawInstanceId)
    {
        // PS: $powerPlan.InstanceID.Split('{')[1].Split('}')[0] - "Microsoft:PowerPlan\{gd}"
        // yields the bare guid. A brace-less value would null-fault in the PS source; the
        // port returns null and lets the "Unknown" fallback take over (accepted deviation).
        if (string.IsNullOrEmpty(rawInstanceId))
        {
            return null;
        }
        int open = rawInstanceId!.IndexOf('{');
        if (open < 0)
        {
            return null;
        }
        string tail = rawInstanceId.Substring(open + 1);
        int close = tail.IndexOf('}');
        return close < 0 ? tail : tail.Substring(0, close);
    }
}
