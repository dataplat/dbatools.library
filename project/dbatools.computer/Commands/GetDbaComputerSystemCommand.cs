#nullable enable

using System;
using System.Collections.Generic;
using System.Management.Automation;
using Dataplat.Dbatools.Connection;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;
using Dataplat.Dbatools.Utility;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves comprehensive hardware and system information from Windows computers hosting
/// SQL Server instances. Port of public/Get-DbaComputerSystem.ps1; surface pinned by
/// migration/baselines/Get-DbaComputerSystem.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaComputerSystem")]
[OutputType(typeof(PSObject))]
public sealed class GetDbaComputerSystemCommand : DbaBaseCmdlet
{
    /// <summary>The target computer(s) to collect system information from; defaults to the local computer.</summary>
    [Parameter(ValueFromPipeline = true, Position = 0)]
    [Alias("cn", "host", "Server")]
    public DbaInstanceParameter[]? ComputerName { get; set; } = DefaultComputerName();

    /// <summary>Alternate credential object to use for accessing the target computer(s).</summary>
    [Parameter(Position = 1)]
    public PSCredential? Credential { get; set; }

    /// <summary>Retrieves additional AWS EC2 metadata when the target computer is hosted on Amazon Web Services.</summary>
    [Parameter]
    public SwitchParameter IncludeAws { get; set; }

    protected override void ProcessRecord()
    {
        if (Interrupted)
        {
            return;
        }

        if (ComputerName is null)
        {
            return;
        }

        foreach (DbaInstanceParameter computer in ComputerName)
        {
            try
            {
                // PS: $server = Resolve-DbaNetworkName -ComputerName $computer.ComputerName -Credential $Credential
                // The nested function's own EnableException stays false (not forwarded), so a
                // resolution failure is ITS "DNS name ... not found" warning plus a null result.
                NetworkResolutionService.NetworkResolutionResult? resolution = null;
                string dnsProbeName = computer.IsLocalHost ? Environment.MachineName : computer.ComputerName;
                bool resolveWarned = false;
                try
                {
                    resolution = NetworkResolutionService.Resolve(new DbaInstanceParameter(computer.ComputerName), Credential, turbo: false);
                }
                catch (PipelineStoppedException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    resolveWarned = true;
                    WriteMessage(MessageLevel.Warning, $"DNS name {dnsProbeName} not found", exception: ex);
                }
                if (resolution is null && !resolveWarned)
                {
                    WriteMessage(MessageLevel.Warning, $"DNS name {dnsProbeName} not found");
                }

                string? computerResolved = resolution?.FullComputerName;

                if (string.IsNullOrEmpty(computerResolved))
                {
                    // PS: Stop-Function -Message "Unable to resolve hostname of $computer. Skipping." -Continue
                    // sits INSIDE the function's own try — under EnableException the throw lands in
                    // the catch below and surfaces as its "Failure" wrap, exactly like PS.
                    if (EnableException.ToBool())
                    {
                        throw new Exception($"Unable to resolve hostname of {computer}. Skipping.");
                    }
                    StopFunction($"Unable to resolve hostname of {computer}. Skipping.", continueLoop: true);
                    continue;
                }

                // PS: Get-DbaCmObject -ClassName Win32_ComputerSystem/Win32_Processor WITHOUT
                // -EnableException: a chain failure is the nested command's own warning (the
                // exact composed message) and a null result; this command keeps going.
                PSObject? computerSystem = QueryFirst(computerResolved!, "Win32_ComputerSystem");
                List<PSObject> computerProcessor = QueryAll(computerResolved!, "Win32_Processor");

                // PS: switch with a default branch — but switch($null) runs NO branch, so a
                // missing value stays null instead of "Unknown".
                object? adminRaw = GetRawValue(computerSystem, "AdminPasswordStatus");
                string? adminPasswordStatus = null;
                if (adminRaw is not null)
                {
                    switch (Convert.ToInt64(adminRaw, System.Globalization.CultureInfo.InvariantCulture))
                    {
                        case 0: adminPasswordStatus = "Disabled"; break;
                        case 1: adminPasswordStatus = "Enabled"; break;
                        case 2: adminPasswordStatus = "Not Implemented"; break;
                        case 3: adminPasswordStatus = "Unknown"; break;
                        default: adminPasswordStatus = "Unknown"; break;
                    }
                }

                // PS: switch with NO default — unmatched (and null) yields $null.
                object? domainRoleRaw = GetRawValue(computerSystem, "DomainRole");
                string? domainRole = null;
                if (domainRoleRaw is not null)
                {
                    switch (Convert.ToInt64(domainRoleRaw, System.Globalization.CultureInfo.InvariantCulture))
                    {
                        case 0: domainRole = "Standalone Workstation"; break;
                        case 1: domainRole = "Member Workstation"; break;
                        case 2: domainRole = "Standalone Server"; break;
                        case 3: domainRole = "Member Server"; break;
                        case 4: domainRole = "Backup Domain Controller"; break;
                        case 5: domainRole = "Primary Domain Controller"; break;
                    }
                }

                // PS: -gt converts $null to 0 on either side.
                bool isHyperThreading = ToLongOrZero(GetRawValue(computerSystem, "NumberOfLogicalProcessors")) > ToLongOrZero(GetRawValue(computerSystem, "NumberOfProcessors"));

                bool isAws = false;
                PSObject? awsProps = null;
                if (IncludeAws.ToBool())
                {
                    string proxiedFunc = BuildProxiedTlsRestMethod();
                    try
                    {
                        RemoteExecutionService.RemoteCommandRequest awsProbe = new()
                        {
                            ComputerName = new DbaInstanceParameter(computerResolved!),
                            Credential = Credential,
                            ArgumentList = new object[] { proxiedFunc },
                            Raw = true,
                            ScriptText = @"Param( $ProxiedFunc )
. ([ScriptBlock]::Create($ProxiedFunc))
((Invoke-TlsRestMethod -TimeoutSec 15 -Uri 'http://169.254.169.254').StatusCode) -eq 200"
                        };
                        RemoteExecutionService.RemoteCommandResult awsProbeResult = RemoteExecutionService.InvokeCommand(awsProbe);
                        ForwardErrors(awsProbeResult.Errors);
                        isAws = LanguagePrimitives.IsTrue(ShapeOutput(awsProbeResult.Output));
                    }
                    catch (PipelineStoppedException)
                    {
                        throw;
                    }
                    catch (Exception ex) when (ChainContainsWebException(ex))
                    {
                        // PS: catch [System.Net.WebException] only — anything else falls to the
                        // outer catch and its "Failure" shape.
                        isAws = false;
                        WriteMessage(MessageLevel.Warning, $"{computerResolved} was not found to be an EC2 instance. Verify http://169.254.169.254 is accessible on the computer.");
                    }

                    if (isAws)
                    {
                        RemoteExecutionService.RemoteCommandRequest awsMetadata = new()
                        {
                            ComputerName = new DbaInstanceParameter(computerResolved!),
                            Credential = Credential,
                            ArgumentList = new object[] { proxiedFunc },
                            ScriptText = @"Param( $ProxiedFunc )
. ([ScriptBlock]::Create($ProxiedFunc))
[PSCustomObject]@{
    AmiId            = (Invoke-TlsRestMethod -Uri 'http://169.254.169.254/latest/meta-data/ami-id')
    IamRoleArn       = ((Invoke-TlsRestMethod -Uri 'http://169.254.169.254/latest/meta-data/iam/info').InstanceProfileArn)
    InstanceId       = (Invoke-TlsRestMethod -Uri 'http://169.254.169.254/latest/meta-data/instance-id')
    InstanceType     = (Invoke-TlsRestMethod -Uri 'http://169.254.169.254/latest/meta-data/instance-type')
    AvailabilityZone = (Invoke-TlsRestMethod -Uri 'http://169.254.169.254/latest/meta-data/placement/availability-zone')
    PublicHostname   = (Invoke-TlsRestMethod -Uri 'http://169.254.169.254/latest/meta-data/public-hostname')
}"
                        };
                        RemoteExecutionService.RemoteCommandResult awsMetadataResult = RemoteExecutionService.InvokeCommand(awsMetadata);
                        ForwardErrors(awsMetadataResult.Errors);
                        object? shaped = ShapeOutput(awsMetadataResult.Output);
                        awsProps = shaped is null ? null : PSObject.AsPSObject(shaped);
                    }
                }

                object? pendingReboot = null;
                try
                {
                    WriteMessage(MessageLevel.Verbose, "Getting information about pending reboots.");
                    pendingReboot = TestPendingReboot(computerResolved!);
                }
                catch (PipelineStoppedException)
                {
                    throw;
                }
                catch
                {
                    WriteMessage(MessageLevel.Verbose, "Not able to get information about pending reboots.");
                }

                object? totalPhysicalMemoryRaw = GetRawValue(computerSystem, "TotalPhysicalMemory");

                PSObject inputObject = new();
                inputObject.Properties.Add(new PSNoteProperty("ComputerName", computerResolved));
                inputObject.Properties.Add(new PSNoteProperty("Domain", GetRawValue(computerSystem, "Domain")));
                inputObject.Properties.Add(new PSNoteProperty("DomainRole", domainRole));
                inputObject.Properties.Add(new PSNoteProperty("Manufacturer", GetRawValue(computerSystem, "Manufacturer")));
                inputObject.Properties.Add(new PSNoteProperty("Model", GetRawValue(computerSystem, "Model")));
                inputObject.Properties.Add(new PSNoteProperty("SystemFamily", GetRawValue(computerSystem, "SystemFamily")));
                inputObject.Properties.Add(new PSNoteProperty("SystemSkuNumber", GetRawValue(computerSystem, "SystemSKUNumber")));
                inputObject.Properties.Add(new PSNoteProperty("SystemType", GetRawValue(computerSystem, "SystemType")));
                inputObject.Properties.Add(new PSNoteProperty("ProcessorName", EnumerateMember(computerProcessor, "Name")));
                inputObject.Properties.Add(new PSNoteProperty("ProcessorCaption", EnumerateMember(computerProcessor, "Caption")));
                inputObject.Properties.Add(new PSNoteProperty("ProcessorMaxClockSpeed", EnumerateMember(computerProcessor, "MaxClockSpeed")));
                inputObject.Properties.Add(new PSNoteProperty("NumberLogicalProcessors", GetRawValue(computerSystem, "NumberOfLogicalProcessors")));
                inputObject.Properties.Add(new PSNoteProperty("NumberProcessors", GetRawValue(computerSystem, "NumberOfProcessors")));
                inputObject.Properties.Add(new PSNoteProperty("IsHyperThreading", isHyperThreading));
                inputObject.Properties.Add(new PSNoteProperty("TotalPhysicalMemory", totalPhysicalMemoryRaw is null ? null : new Size(Convert.ToInt64(totalPhysicalMemoryRaw, System.Globalization.CultureInfo.InvariantCulture))));
                inputObject.Properties.Add(new PSNoteProperty("IsDaylightSavingsTime", GetRawValue(computerSystem, "EnableDaylightSavingsTime")));
                inputObject.Properties.Add(new PSNoteProperty("DaylightInEffect", GetRawValue(computerSystem, "DaylightInEffect")));
                inputObject.Properties.Add(new PSNoteProperty("DnsHostName", GetRawValue(computerSystem, "DNSHostName")));
                inputObject.Properties.Add(new PSNoteProperty("IsSystemManagedPageFile", GetRawValue(computerSystem, "AutomaticManagedPagefile")));
                inputObject.Properties.Add(new PSNoteProperty("AdminPasswordStatus", adminPasswordStatus));
                inputObject.Properties.Add(new PSNoteProperty("PendingReboot", pendingReboot));

                if (IncludeAws.ToBool() && isAws)
                {
                    inputObject.Properties.Add(new PSNoteProperty("AwsAmiId", GetRawValue(awsProps, "AmiId")));
                    inputObject.Properties.Add(new PSNoteProperty("AwsIamRoleArn", GetRawValue(awsProps, "IamRoleArn")));
                    inputObject.Properties.Add(new PSNoteProperty("AwsEc2InstanceId", GetRawValue(awsProps, "InstanceId")));
                    inputObject.Properties.Add(new PSNoteProperty("AwsEc2InstanceType", GetRawValue(awsProps, "InstanceType")));
                    inputObject.Properties.Add(new PSNoteProperty("AwsAvailabilityZone", GetRawValue(awsProps, "AvailabilityZone")));
                    inputObject.Properties.Add(new PSNoteProperty("AwsPublicHostName", GetRawValue(awsProps, "PublicHostname")));
                }

                // PS: Select-DefaultView -ExcludeProperty builds the display set from
                // Get-Member output, which is ALPHABETICALLY sorted — not declaration order.
                string[] excludes = { "SystemSkuNumber", "IsDaylightSavingsTime", "DaylightInEffect", "DnsHostName", "AdminPasswordStatus" };
                List<string> displayNames = new();
                foreach (PSPropertyInfo property in inputObject.Properties)
                {
                    if (Array.FindIndex(excludes, e => string.Equals(e, property.Name, StringComparison.OrdinalIgnoreCase)) < 0)
                    {
                        displayNames.Add(property.Name);
                    }
                }
                displayNames.Sort(StringComparer.OrdinalIgnoreCase);
                OutputHelper.SetDefaultDisplayPropertySet(inputObject, displayNames.ToArray());
                WriteObject(inputObject);
            }
            catch (PipelineStoppedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // PS: catch { Stop-Function -Message "Failure" -ErrorRecord $_ -Target $computer -Continue }
                StopFunction("Failure", target: computer, errorRecord: new ErrorRecord(ex, "dbatools_Get-DbaComputerSystem", ErrorCategory.NotSpecified, computer), continueLoop: true);
                continue;
            }
        }
    }

    // PS: Test-Bound "Credential" branches to the same call with/without -Credential.
    private CimService.CmObjectResult Query(string computerResolved, string className)
    {
        CimService.CmObjectRequest request = new()
        {
            ComputerName = new DbaInstanceParameter(computerResolved).ComputerName,
            ClassName = className
        };
        if (TestBound(nameof(Credential)))
        {
            request.Credential = Credential;
        }
        return CimService.GetCmObject(request);
    }

    private PSObject? QueryFirst(string computerResolved, string className)
    {
        List<PSObject> instances = QueryAll(computerResolved, className);
        return instances.Count > 0 ? instances[0] : null;
    }

    private List<PSObject> QueryAll(string computerResolved, string className)
    {
        try
        {
            CimService.CmObjectResult result = Query(computerResolved, className);
            ForwardErrors(result.PassthroughErrors);
            return result.Instances;
        }
        catch (PipelineStoppedException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The nested Get-DbaCmObject ran WITHOUT -EnableException: its Stop-Function is a
            // warning with the exact composed message (CimService throws that same text), and
            // the caller proceeds with a null/empty result — even under this command's own
            // -EnableException (the nested function's parameter was never forwarded).
            WriteMessage(MessageLevel.Warning, ex.Message, exception: ex);
            return new List<PSObject>();
        }
    }

    private void ForwardErrors(List<ErrorRecord> errors)
    {
        foreach (ErrorRecord error in errors)
        {
            WriteError(error);
        }
    }

    // PS: "function Invoke-TlsRestMethod {`n" + $(Get-Item function:\Invoke-TlsRestMethod).ScriptBlock + "`n}"
    // The private function lives inside the dbatools module scope; resolve it there.
    private string BuildProxiedTlsRestMethod()
    {
        System.Collections.ObjectModel.Collection<PSObject> results = InvokeCommand.InvokeScript("& (Get-Module dbatools) { (Get-Item function:\\Invoke-TlsRestMethod).ScriptBlock }");
        string body = results.Count > 0 && results[0] is not null ? results[0].ToString() : string.Empty;
        return "function Invoke-TlsRestMethod {\n" + body + "\n}";
    }

    // private/functions/Test-PendingReboot.ps1 ported in-process: three registry probes over
    // the compiled Invoke-Command2 with -Raw and ErrorAction Stop (non-terminating errors
    // become terminating for the caller's try/catch).
    private object TestPendingReboot(string computerResolved)
    {
        object? cbs = InvokeRegistryProbe(computerResolved, "Get-ItemProperty -Path 'HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Component Based Servicing' -Name 'RebootPending' -ErrorAction SilentlyContinue");
        if (LanguagePrimitives.IsTrue(cbs))
        {
            WriteMessage(MessageLevel.Verbose, "Reboot pending detected in the Component Based Servicing registry key");
            return true;
        }

        // Query WUAU from the registry
        object? wuau = InvokeRegistryProbe(computerResolved, "Get-ItemProperty -Path 'HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\WindowsUpdate\\Auto Update' -Name 'RebootRequired' -ErrorAction SilentlyContinue");
        if (LanguagePrimitives.IsTrue(wuau))
        {
            WriteMessage(MessageLevel.Verbose, "WUAU has a reboot pending");
            return true;
        }

        // Query PendingFileRenameOperations from the registry (the caller never passes
        // -NoPendingRename, so the check always runs).
        object? rename = InvokeRegistryProbe(computerResolved, "Get-ItemProperty -Path 'HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Session Manager' -Name 'PendingFileRenameOperations' -ErrorAction SilentlyContinue");
        if (LanguagePrimitives.IsTrue(rename) && LanguagePrimitives.IsTrue(GetRawValue(rename is null ? null : PSObject.AsPSObject(rename), "PendingFileRenameOperations")))
        {
            WriteMessage(MessageLevel.Verbose, "Reboot pending in the PendingFileRenameOperations registry value");
            return true;
        }
        return false;
    }

    private object? InvokeRegistryProbe(string computerResolved, string scriptText)
    {
        RemoteExecutionService.RemoteCommandRequest request = new()
        {
            ComputerName = new DbaInstanceParameter(computerResolved),
            Credential = Credential,
            Raw = true,
            ScriptText = scriptText
        };
        RemoteExecutionService.RemoteCommandResult result = RemoteExecutionService.InvokeCommand(request);
        if (result.Errors.Count > 0)
        {
            // PS: ErrorAction Stop on the Invoke-Command2 splat.
            throw result.Errors[0].Exception;
        }
        return ShapeOutput(result.Output);
    }

    // PS pipeline-assignment shape: empty -> null, one -> the scalar, many -> object[].
    private static object? ShapeOutput(List<PSObject> output)
    {
        if (output.Count == 0)
        {
            return null;
        }
        if (output.Count == 1)
        {
            return output[0];
        }
        return output.ToArray();
    }

    // PS member enumeration over the Win32_Processor result: null input -> null, one
    // instance -> the scalar property value, several -> object[] of the values.
    private static object? EnumerateMember(List<PSObject> instances, string name)
    {
        if (instances.Count == 0)
        {
            return null;
        }
        if (instances.Count == 1)
        {
            return GetRawValue(instances[0], name);
        }
        List<object?> values = new();
        foreach (PSObject instance in instances)
        {
            values.Add(GetRawValue(instance, name));
        }
        return values.ToArray();
    }

    private static object? GetRawValue(PSObject? instance, string name)
    {
        return instance?.Properties[name]?.Value;
    }

    private static long ToLongOrZero(object? value)
    {
        if (value is null)
        {
            return 0;
        }
        try
        {
            return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch
        {
            return 0;
        }
    }

    private static bool ChainContainsWebException(Exception ex)
    {
        Exception? current = ex;
        while (current is not null)
        {
            if (current is System.Net.WebException)
            {
                return true;
            }
            current = current.InnerException;
        }
        return false;
    }

    private static DbaInstanceParameter[]? DefaultComputerName()
    {
        string? machine = Environment.GetEnvironmentVariable("COMPUTERNAME");
        if (string.IsNullOrEmpty(machine))
        {
            return null;
        }
        return new[] { new DbaInstanceParameter(machine) };
    }
}
