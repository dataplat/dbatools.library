#nullable enable
#pragma warning disable CA1416 // Windows-only command

using System;
using System.Collections.Generic;
using System.Management.Automation;
using Dataplat.Dbatools.Connection;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Enables Force Network Encryption for a SQL Server instance by setting ForceEncryption in the
/// SuperSocketNetLib registry key (takes effect on the next SQL restart). Port of
/// public/Enable-DbaForceNetworkEncryption.ps1; surface pinned by
/// migration/baselines/Enable-DbaForceNetworkEncryption.json.
/// </summary>
[Cmdlet(VerbsLifecycle.Enable, "DbaForceNetworkEncryption", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Low, DefaultParameterSetName = "Default")]
[OutputType(typeof(PSObject))]
public sealed class EnableDbaForceNetworkEncryptionCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance(s); defaults to the local computer.</summary>
    [Parameter(ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; } = DefaultSqlInstance();

    /// <summary>Alternate Windows credential for the target computer.</summary>
    [Parameter(Position = 1)]
    public PSCredential? Credential { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // PS scriptblock, verbatim: sets ForceEncryption=$true and returns the 5-prop status object
    // with the TARGET's $env:COMPUTERNAME.
    private const string SetScript = @"
                $regPath = ""Registry::HKEY_LOCAL_MACHINE\$($args[0])\MSSQLServer\SuperSocketNetLib""
                $cert = (Get-ItemProperty -Path $regPath -Name Certificate).Certificate
                #Variable marked as unused by PSScriptAnalyzer
                #$oldvalue = (Get-ItemProperty -Path $regPath -Name ForceEncryption).ForceEncryption
                Set-ItemProperty -Path $regPath -Name ForceEncryption -Value $true
                $forceencryption = (Get-ItemProperty -Path $regPath -Name ForceEncryption).ForceEncryption

                [PSCustomObject]@{
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

        if (SqlInstance is null)
        {
            return;
        }

        foreach (DbaInstanceParameter instance in SqlInstance)
        {
            if (instance is null)
            {
                continue;
            }
            WriteMessage(MessageLevel.VeryVerbose, $"Processing {instance}.", target: instance);

            if (!ForceEncryptionResolver.RequireElevationSatisfied(instance))
            {
                StopFunction("Console not elevated, but elevation is required to perform some actions on localhost for this command.", target: instance, continueLoop: true);
                continue;
            }

            WriteMessage(MessageLevel.Verbose, "Resolving hostname.");
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
                StopFunction($"Can't resolve {instance}.", target: instance, category: ErrorCategory.InvalidArgument, continueLoop: true);
                continue;
            }

            string fullComputerName = resolved.FullComputerName;
            ForceEncryptionResolver.Outcome outcome = ForceEncryptionResolver.Resolve(fullComputerName, instance, new PSCredentialWrap(Credential), out ForceEncryptionResolver.Resolution res, out Exception? accessEx);
            if (outcome == ForceEncryptionResolver.Outcome.AccessFailed)
            {
                StopFunction($"Failed to access {instance}", target: instance, exception: accessEx, continueLoop: true);
                continue;
            }
            if (outcome == ForceEncryptionResolver.Outcome.InstanceNotFound)
            {
                StopFunction($"Can't find instance {res.Vsname} on {instance}.", target: instance, category: ErrorCategory.ObjectNotFound, continueLoop: true);
                continue;
            }

            // PS: if ([String]::IsNullOrEmpty($vsname)) { $vsname = $instance }
            object vsnameArg = string.IsNullOrEmpty(res.Vsname) ? (object)instance : res.Vsname!;

            WriteMessage(MessageLevel.Verbose, $"Regroot: {res.RegRoot}", target: instance);
            WriteMessage(MessageLevel.Verbose, $"ServiceAcct: {res.ServiceAccount}", target: instance);
            WriteMessage(MessageLevel.Verbose, $"InstanceName: {res.InstanceName}", target: instance);
            WriteMessage(MessageLevel.Verbose, $"VSNAME: {vsnameArg}", target: instance);

            // PS: if ($PScmdlet.ShouldProcess("local", "Connecting to $instance to modify the
            //     ForceEncryption value in $regRoot for $($instance.InstanceName)"))
            if (!ShouldProcess("local", $"Connecting to {instance} to modify the ForceEncryption value in {res.RegRoot} for {instance.InstanceName}"))
            {
                continue;
            }

            try
            {
                RemoteExecutionService.RemoteCommandRequest request = new()
                {
                    ComputerName = new DbaInstanceParameter(fullComputerName),
                    Credential = Credential,
                    ScriptText = SetScript,
                    ArgumentList = new object?[] { res.RegRoot, vsnameArg, res.InstanceName }!
                };
                RemoteExecutionService.RemoteCommandResult result = RemoteExecutionService.InvokeCommand(request);
                if (result.Errors.Count > 0)
                {
                    StopFunction($"Failed to connect to {fullComputerName} using PowerShell remoting", target: instance, errorRecord: result.Errors[0], continueLoop: true);
                    continue;
                }
                // PS pipes the cooked output through an ADDITIONAL Select-Object * -ExcludeProperty
                // PSComputerName, RunspaceId, PSShowComputerName; replicate it so the projection
                // typename matches the function exactly.
                foreach (PSObject output in ReSelect(result.Output))
                {
                    if (output is not null)
                    {
                        WriteObject(output);
                    }
                }
                // PS: Write-Message -Level Critical "... You must now restart the SQL Server ..."
                WriteMessage(MessageLevel.Critical, $"Force encryption was successfully set on {fullComputerName} for the {res.InstanceName} instance. You must now restart the SQL Server for changes to take effect.", target: instance);
            }
            catch (PipelineStoppedException)
            {
                throw;
            }
            catch (RuntimeException rex)
            {
                StopFunction($"Failed to connect to {fullComputerName} using PowerShell remoting", target: instance, errorRecord: rex.ErrorRecord, continueLoop: true);
                continue;
            }
            catch (Exception ex)
            {
                StopFunction($"Failed to connect to {fullComputerName} using PowerShell remoting", target: instance, exception: ex, continueLoop: true);
                continue;
            }
        }
    }

    // PS: ... | Select-Object -Property * -ExcludeProperty PSComputerName, RunspaceId, PSShowComputerName
    private IEnumerable<PSObject> ReSelect(List<PSObject> input)
    {
        using PowerShell shell = PowerShell.Create(RunspaceMode.CurrentRunspace);
        shell.AddCommand("Select-Object")
            .AddParameter("Property", new[] { "*" })
            .AddParameter("ExcludeProperty", new[] { "PSComputerName", "RunspaceId", "PSShowComputerName" });
        return shell.Invoke(input);
    }

    private static DbaInstanceParameter[]? DefaultSqlInstance()
    {
        string? machine = Environment.GetEnvironmentVariable("COMPUTERNAME");
        if (string.IsNullOrEmpty(machine))
        {
            return null;
        }
        return new[] { new DbaInstanceParameter(machine) };
    }
}
