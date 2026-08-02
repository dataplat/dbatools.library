#nullable enable
#pragma warning disable CA1416 // Windows-only command: SQL WMI ManagedComputer, WindowsPrincipal, PSRemoting registry read

using System;
using System.Collections;
using System.Management.Automation;
using System.Security.Principal;
using Dataplat.Dbatools.Configuration;
using Dataplat.Dbatools.Connection;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;
using Microsoft.SqlServer.Management.Smo.Wmi;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves the Force Network Encryption setting and associated certificate from SQL Server's
/// network configuration in the Windows registry (SuperSocketNetLib). Port of
/// public/Get-DbaForceNetworkEncryption.ps1; surface pinned by
/// migration/baselines/Get-DbaForceNetworkEncryption.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaForceNetworkEncryption")]
[OutputType(typeof(PSObject))]
public sealed class GetDbaForceNetworkEncryptionCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances. Defaults to localhost.</summary>
    [Parameter(ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[] SqlInstance { get; set; } = DefaultSqlInstance();

    /// <summary>Allows you to login to the computer (not sql instance) using alternative Windows credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? Credential { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // PS: the registry read runs verbatim on the TARGET via Invoke-Command2, so $env:COMPUTERNAME
    // resolves on the remote machine (ComputerName = e.g. SQL01) and the returned @{} is cast to
    // PSCustomObject on the client. The compiled port keeps this exact transport (RemoteExecutionService
    // = Invoke-Command2) and re-emits the returned hashtable by enumerating it in place, so the
    // @{}-cast property order is preserved (a case-insensitive Hashtable's bucket order:
    // byte-deterministic on net472, per-process randomized on net8.0 - same non-determinism the PS
    // function itself has on PS7). The comment below is preserved verbatim from the PS source.
    private const string RegistryScript = @"
                $regPath = ""Registry::HKEY_LOCAL_MACHINE\$($args[0])\MSSQLServer\SuperSocketNetLib""
                $cert = (Get-ItemProperty -Path $regPath -Name Certificate).Certificate
                $forceencryption = (Get-ItemProperty -Path $regPath -Name ForceEncryption).ForceEncryption

                # [PSCustomObject] doesn't always work, unsure why. so return hashtable then turn it into  PSCustomObject on client
                @{
                    ComputerName          = $env:COMPUTERNAME
                    InstanceName          = $args[2]
                    SqlInstance           = $args[1]
                    ForceEncryption       = ($forceencryption -eq $true)
                    CertificateThumbprint = $cert
                }";

    protected override void ProcessRecord()
    {
        if (Interrupted)
        {
            return;
        }

        foreach (DbaInstanceParameter instance in SqlInstance)
        {
            WriteMessage(MessageLevel.VeryVerbose, $"Processing {instance}", target: instance);

            // PS: $null = Test-ElevationRequirement -ComputerName $instance -Continue
            // Only localhost-without-elevation fails; a remote target is a pass-through.
            if (!RequireElevationSatisfied(instance))
            {
                continue;
            }

            // PS: try { Resolve-DbaNetworkName ... -EnableException } catch { Resolve-DbaNetworkName ... -Turbo }
            // NetworkResolutionService.Resolve returns null on DNS failure rather than throwing, so the
            // catch only fires on an unexpected error; either way a null result -> Stop-Function.
            NetworkResolutionService.NetworkResolutionResult? resolved;
            try
            {
                resolved = NetworkResolutionService.Resolve(instance, Credential, turbo: false);
            }
            catch (PipelineStoppedException)
            {
                throw;
            }
            catch
            {
                resolved = NetworkResolutionService.Resolve(instance, Credential, turbo: true);
            }

            if (resolved is null)
            {
                StopFunction($"Can't resolve {instance}", target: instance, category: ErrorCategory.InvalidArgument, continueLoop: true);
                continue;
            }

            string fullComputerName = resolved.FullComputerName;

            // PS: $sqlwmi = Invoke-ManagedComputerCommand -ComputerName $resolved.FullComputerName
            //     -ScriptBlock { $wmi.Services } | Where-Object DisplayName -eq "SQL Server ($($instance.InstanceName))"
            Service? sqlwmi = null;
            try
            {
                ManagedComputer wmi = BuildManagedComputer(fullComputerName);
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
            catch (PipelineStoppedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                StopFunction($"Failed to access {instance}", target: instance, exception: ex, continueLoop: true);
                continue;
            }

            // PS: $regRoot = (AdvancedProperties | Where Name -eq REGROOT).Value ; $vsname = (... VSNAME).Value
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

                // PS: try { $instanceName = $sqlwmi.DisplayName.Replace('SQL Server (', '').Replace(')', '') } catch { $null = 1 }
                try
                {
                    instanceName = sqlwmi.DisplayName.Replace("SQL Server (", "").Replace(")", "");
                }
                catch
                {
                    // Probably because the instance name has been aliased or does not exist or something
                    // here to avoid an empty catch
                }

                serviceAccount = sqlwmi.ServiceAccount;
            }

            // PS: if ([String]::IsNullOrEmpty($regRoot)) { fallback via -match on the AdvancedProperty
            //     ToString(), then -Split 'Value=' [1]; else Stop-Function "Can't find instance ..." }
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
                    StopFunction($"Can't find instance {vsname} on {instance}", target: instance, category: ErrorCategory.ObjectNotFound, continueLoop: true);
                    continue;
                }
            }

            // PS: if ([String]::IsNullOrEmpty($vsname)) { $vsname = $instance } - keeps the DbaInstanceParameter
            // object as the scriptblock's $args[1], exactly as the PS binder serialized it over the wire.
            object vsnameArg = string.IsNullOrEmpty(vsname) ? (object)instance : vsname!;

            WriteMessage(MessageLevel.Verbose, $"Regroot: {regRoot}", target: instance);
            WriteMessage(MessageLevel.Verbose, $"ServiceAcct: {serviceAccount}", target: instance);
            WriteMessage(MessageLevel.Verbose, $"InstanceName: {instanceName}", target: instance);
            WriteMessage(MessageLevel.Verbose, $"VSNAME: {vsnameArg}", target: instance);

            // PS: $results = Invoke-Command2 -ComputerName $resolved.FullComputerName -Credential $Credential
            //     -ArgumentList $regRoot, $vsname, $instanceName -ScriptBlock $scriptBlock -ErrorAction Stop -Raw
            RemoteExecutionService.RemoteCommandResult regResult;
            try
            {
                RemoteExecutionService.RemoteCommandRequest request = new RemoteExecutionService.RemoteCommandRequest
                {
                    ComputerName = new DbaInstanceParameter(fullComputerName),
                    Credential = Credential,
                    ScriptText = RegistryScript,
                    // regRoot is non-null here (found directly or via the fallback, else we continued);
                    // instanceName is non-null in practice (DisplayName always parses) - a null element
                    // would still serialize as $null, exactly as the PS -ArgumentList binder did.
                    ArgumentList = new object[] { regRoot!, vsnameArg, instanceName! },
                    Raw = true
                };
                regResult = RemoteExecutionService.InvokeCommand(request);
            }
            catch (PipelineStoppedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                StopFunction($"Failed to connect to {fullComputerName} using PowerShell remoting", target: instance, exception: ex, continueLoop: true);
                continue;
            }

            // PS: -ErrorAction Stop makes any remote error terminating -> the catch above. The compiled
            // Invoke-Command2 surfaces non-terminating remote errors in Errors instead of throwing, so a
            // populated Errors bag maps to the same Stop-Function path (first record, like -ErrorRecord $_).
            if (regResult.Errors.Count > 0)
            {
                StopFunction($"Failed to connect to {fullComputerName} using PowerShell remoting", target: instance, errorRecord: regResult.Errors[0], continueLoop: true);
                continue;
            }

            // PS: foreach ($result in $results) { [PSCustomObject]$result }
            foreach (PSObject output in regResult.Output)
            {
                if (output is null)
                {
                    continue;
                }
                WriteObject(BuildOutput(output));
            }
        }
    }

    // [PSCustomObject]$result parity: the -Raw remote return is a Hashtable; enumerate it in its native
    // (bucket) order and add one PSNoteProperty per entry - identical to what the PS cast does on the
    // same runtime. A non-dictionary payload (never happens for the @{} return) is re-emitted with its
    // own property order.
    private static PSObject BuildOutput(PSObject output)
    {
        PSObject result = new PSObject();
        if (output.BaseObject is IDictionary dict)
        {
            foreach (DictionaryEntry entry in dict)
            {
                result.Properties.Add(new PSNoteProperty((string)entry.Key, entry.Value));
            }
        }
        else
        {
            foreach (PSPropertyInfo prop in output.Properties)
            {
                result.Properties.Add(new PSNoteProperty(prop.Name, prop.Value));
            }
        }
        return result;
    }

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

    // PS: $null = Test-ElevationRequirement -ComputerName $instance -Continue. The disable config short-
    // circuits to a pass; otherwise only a localhost target without an elevated console fails (and warns
    // under the caller's own name, which the compiled StopFunction already does).
    private bool RequireElevationSatisfied(DbaInstanceParameter instance)
    {
        if (GetConfigTruthy("commands.test-elevationrequirement.disable"))
        {
            return true;
        }
        bool isElevated = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        if (instance.IsLocalHost && !isElevated)
        {
            StopFunction("Console not elevated, but elevation is required to perform some actions on localhost for this command.", continueLoop: true);
            return false;
        }
        return true;
    }

    // PS: ($value -Split 'Value=')[1] - the substring after the first "Value=", or $null when absent.
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
                return LanguagePrimitives.IsTrue(config.Value);
            }
            catch
            {
                // malformed configuration values fall back to false, like Get-DbatoolsConfigValue -Fallback
            }
        }
        return false;
    }

    // PS: [DbaInstanceParameter[]]$SqlInstance = $env:COMPUTERNAME
    private static DbaInstanceParameter[] DefaultSqlInstance()
    {
        string? machine = Environment.GetEnvironmentVariable("COMPUTERNAME");
        if (string.IsNullOrEmpty(machine))
        {
            machine = Environment.MachineName;
        }
        return new[] { new DbaInstanceParameter(machine) };
    }
}
