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
/// Retrieves SQL Server client network protocol configuration and status (Named Pipes,
/// TCP/IP, Shared Memory, VIA) from local or remote computers, with Enable()/Disable()
/// script methods carried on each emitted protocol. Port of public/Get-DbaClientProtocol.ps1;
/// surface pinned by migration/baselines/Get-DbaClientProtocol.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaClientProtocol")]
[OutputType(typeof(PSObject))]
public sealed class GetDbaClientProtocolCommand : DbaBaseCmdlet
{
    /// <summary>The target computer(s) to retrieve client protocol configuration from; defaults to the local computer.</summary>
    [Parameter(ValueFromPipeline = true, Position = 0)]
    [Alias("cn", "host", "Server")]
    public DbaInstanceParameter[]? ComputerName { get; set; } = DefaultComputerName();

    /// <summary>Credential object used to connect to the computer as a different user.</summary>
    [Parameter(Position = 1)]
    public PSCredential? Credential { get; set; }

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

        // PS: foreach ($computer in $ComputerName.ComputerName) — the loop value is the
        // bare host-name string; the "Failed to connect" message below uses it as it was
        // BEFORE the FullComputerName reassignment inside the success branch.
        foreach (DbaInstanceParameter computer in ComputerName)
        {
            // PS member enumeration ($ComputerName.ComputerName) skips null array slots.
            if (computer is null)
            {
                continue;
            }
            string originalName = computer.ComputerName;

            // PS: $server = Resolve-DbaNetworkName -ComputerName $computer -Credential $credential
            // (no EnableException forwarded): a DNS failure is Resolve-DbaNetworkName's own
            // "DNS name ... not found" warning plus a null result. NetworkResolutionService.Resolve
            // returns null on that same Stop-Function -Continue path, so emit the warning here.
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

            // PS: if ($server.FullComputerName) { ... } else { "Failed to connect to $computer" }
            if (string.IsNullOrEmpty(computerResolved))
            {
                WriteMessage(MessageLevel.Warning, $"Failed to connect to {originalName}");
                continue;
            }

            // PS: $computer = $server.FullComputerName — every subsequent message uses the full name.
            WriteMessage(MessageLevel.Verbose, $"Getting SQL Server namespace on {computerResolved}");

            // PS: Get-DbaCmObject -Namespace root\Microsoft\SQLServer -Query "..." -ErrorAction
            // SilentlyContinue | Sort-Object Name -Descending | Select-Object -First 1
            List<PSObject> namespaces = QueryNamespaces(computerResolved!);
            string? namespaceName = SelectHighestNamespaceName(namespaces);

            // PS: if ($namespace.Name) { ... } else { "No ComputerManagement Namespace ..." }
            if (!string.IsNullOrEmpty(namespaceName))
            {
                WriteMessage(MessageLevel.Verbose, $"Getting Cim class ClientNetworkProtocol in Namespace {namespaceName} on {computerResolved}");
                try
                {
                    // PS: $prot = Get-DbaCmObject -Namespace ("root\Microsoft\SQLServer\" + name)
                    //     -ClassName ClientNetworkProtocol -ErrorAction SilentlyContinue
                    // A chain failure warns (the exact composed message) and returns nothing WITHOUT
                    // throwing (non-EnableException Stop-Function), so the catch below never fires for
                    // it and the foreach simply iterates zero times.
                    List<PSObject> protocols = ReadProtocols(computerResolved!, @"root\Microsoft\SQLServer\" + namespaceName);

                    foreach (PSObject protocol in protocols)
                    {
                        // PS: $prot | Add-Member -Force ScriptProperty IsEnabled + ScriptMethod
                        // Enable/Disable (script text carried verbatim so the returned objects keep
                        // their live Enable()/Disable() behavior over Invoke-CimMethod).
                        AddProtocolMembers(protocol);

                        // PS: Select-DefaultView -InputObject $protocol -Property
                        //   'PSComputerName as ComputerName', 'ProtocolDisplayName as DisplayName',
                        //   'ProtocolDll as DLL', 'ProtocolOrder as Order', 'IsEnabled'
                        OutputHelper.AddAliasProperty(protocol, "ComputerName", "PSComputerName");
                        OutputHelper.AddAliasProperty(protocol, "DisplayName", "ProtocolDisplayName");
                        OutputHelper.AddAliasProperty(protocol, "DLL", "ProtocolDll");
                        OutputHelper.AddAliasProperty(protocol, "Order", "ProtocolOrder");
                        OutputHelper.SetDefaultDisplayPropertySet(protocol, "ComputerName", "DisplayName", "DLL", "Order", "IsEnabled");
                        WriteObject(protocol);
                    }
                }
                catch (PipelineStoppedException)
                {
                    throw;
                }
                catch
                {
                    // PS: catch { Write-Message -Level Warning -Message "No Sql ClientNetworkProtocol found on $computer" }
                    WriteMessage(MessageLevel.Warning, $"No Sql ClientNetworkProtocol found on {computerResolved}");
                }
            }
            else
            {
                // PS: Write-Message -Level Warning -Message "No ComputerManagement Namespace on $computer. Please note that this function is available from SQL 2005 up."
                WriteMessage(MessageLevel.Warning, $"No ComputerManagement Namespace on {computerResolved}. Please note that this function is available from SQL 2005 up.");
            }
        }
    }

    // PS: Get-DbaCmObject -Namespace root\Microsoft\SQLServer -Query "..." -ErrorAction
    // SilentlyContinue, with NO -EnableException. The nested command owns its failure: a chain
    // failure surfaces as its Stop-Function warning (the exact composed message) and an empty
    // result. -ErrorAction SilentlyContinue silences only the PowerShellRemoting-rung passthrough
    // error stream (Stop-Function's warning rides the warning stream, unaffected), so the
    // PassthroughErrors are NOT re-emitted here.
    private List<PSObject> QueryNamespaces(string computerResolved)
    {
        try
        {
            CimService.CmObjectRequest request = new()
            {
                ComputerName = new DbaInstanceParameter(computerResolved).ComputerName,
                Namespace = @"root\Microsoft\SQLServer",
                Query = "Select * FROM __NAMESPACE WHERE Name LIke 'ComputerManagement%'"
            };
            // PS: the two Get-DbaCmObject CIM calls run under the CURRENT security context - only
            // Resolve-DbaNetworkName gets -Credential (codex parity fix 2026-07-10).
            CimService.CmObjectResult result = CimService.GetCmObject(request);
            return result.Instances;
        }
        catch (PipelineStoppedException)
        {
            throw;
        }
        catch (Exception ex)
        {
            WriteMessage(MessageLevel.Warning, ex.Message, exception: ex);
            return new List<PSObject>();
        }
    }

    // PS: Get-DbaCmObject -Namespace <root\Microsoft\SQLServer\ComputerManagementNN>
    // -ClassName ClientNetworkProtocol -ErrorAction SilentlyContinue, NO -EnableException.
    // Same nested-command discipline as QueryNamespaces.
    private List<PSObject> ReadProtocols(string computerResolved, string cimNamespace)
    {
        try
        {
            CimService.CmObjectRequest request = new()
            {
                ComputerName = new DbaInstanceParameter(computerResolved).ComputerName,
                Namespace = cimNamespace,
                ClassName = "ClientNetworkProtocol"
            };
            // PS runs this CIM call under the current security context, no -Credential (codex fix).
            CimService.CmObjectResult result = CimService.GetCmObject(request);
            return result.Instances;
        }
        catch (PipelineStoppedException)
        {
            throw;
        }
        catch (Exception ex)
        {
            WriteMessage(MessageLevel.Warning, ex.Message, exception: ex);
            return new List<PSObject>();
        }
    }

    // PS: | Sort-Object Name -Descending | Select-Object -First 1  then  $namespace.Name.
    // Descending order over the __NAMESPACE Name property picks the highest ComputerManagement<NN>
    // (newest SQL). Sort-Object's default comparer is CASE-INSENSITIVE, so use OrdinalIgnoreCase
    // (codex parity fix 2026-07-10: a mixed-case namespace pair would have flipped an Ordinal
    // compare).
    private static string? SelectHighestNamespaceName(List<PSObject> namespaces)
    {
        string? best = null;
        foreach (PSObject ns in namespaces)
        {
            string? name = ns.Properties["Name"]?.Value?.ToString();
            if (name is null)
            {
                continue;
            }
            if (best is null || string.Compare(name, best, StringComparison.OrdinalIgnoreCase) > 0)
            {
                best = name;
            }
        }
        return best;
    }

    // PS Add-Member -Force per protocol: replace any same-named member, then attach the
    // IsEnabled script property and the Enable/Disable script methods with the PS script text
    // verbatim. ClientNetworkProtocol carries no native member of these names, so the -Force
    // remove is a no-op in practice; it is kept for exact parity.
    private static void AddProtocolMembers(PSObject protocol)
    {
        protocol.Members.Remove("IsEnabled");
        protocol.Properties.Add(new PSScriptProperty("IsEnabled",
            ScriptBlock.Create("switch ( $this.ProtocolOrder ) { 0 { $false } default { $true } }")));

        protocol.Members.Remove("Enable");
        protocol.Methods.Add(new PSScriptMethod("Enable",
            ScriptBlock.Create("Invoke-CimMethod -MethodName SetEnable -InputObject $this")));

        protocol.Members.Remove("Disable");
        protocol.Methods.Add(new PSScriptMethod("Disable",
            ScriptBlock.Create("Invoke-CimMethod -MethodName SetDisable -InputObject $this")));
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
