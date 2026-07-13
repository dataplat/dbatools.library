#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Management.Automation;
using Dataplat.Dbatools.Connection;
using Dataplat.Dbatools.Parameter;
using Microsoft.SqlServer.Management.Smo;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Gets the SMO Resource Governor object. Port of public/Get-DbaResourceGovernor.ps1
/// (W1-095). Each instance rides one VERBATIM module hop: the $server.ResourceGovernor
/// read, the truthiness-gated Add-Member -Force triplet, and the unconditional
/// Select-DefaultView -InputObject emission (a null governor feeds the binder error
/// through the W1-045 2>&1 re-emission). Surface pinned by
/// migration/baselines/Get-DbaResourceGovernor.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaResourceGovernor")]
public sealed class GetDbaResourceGovernorCommand : DbaInstanceCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public override DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public override PSCredential? SqlCredential { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        foreach (DbaInstanceParameter instance in SqlInstance)
        {
            Hashtable connectParams = new Hashtable();
            connectParams["SqlInstance"] = instance;
            connectParams["SqlCredential"] = SqlCredential;
            connectParams["MinimumVersion"] = 10;
            NestedConnect.Outcome connection = NestedConnect.Connect(this, connectParams);
            if (!connection.Ok)
            {
                StopFunction("Failure", target: instance, errorRecord: connection.Failure, category: ErrorCategory.ConnectionError, continueLoop: true);
                continue;
            }
            Server server = connection.Server!;

            try
            {
                foreach (PSObject? item in NestedCommand.InvokeScoped(this, GovernorScript, server))
                {
                    if (item?.BaseObject is ErrorRecord nestedError)
                    {
                        RemoveHopErrorBookkeeping(nestedError);
                        WriteError(nestedError);
                    }
                    else
                    {
                        WriteObject(item);
                    }
                }
            }
            catch (PipelineStoppedException)
            {
                throw;
            }
            catch (RuntimeException ex)
            {
                StatementFault.Surface(this, ex, "Get-DbaResourceGovernor");
            }
        }
    }

    /// <summary>Removes the silent $error copy the nested pipeline bagged for a merged-back
    /// non-terminating record (the W1-045 compensation).</summary>
    private void RemoveHopErrorBookkeeping(ErrorRecord record)
    {
        try
        {
            if (SessionState.PSVariable.GetValue("Error") is not ArrayList errorList || errorList.Count == 0)
                return;
            if (errorList[0] is not ErrorRecord first)
                return;
            if (ReferenceEquals(first, record) || ReferenceEquals(first.Exception, record.Exception) ||
                string.Equals(first.Exception?.Message, record.Exception?.Message, StringComparison.Ordinal))
            {
                errorList.RemoveAt(0);
            }
        }
        catch
        {
            // best-effort bookkeeping
        }
    }

    // PS: the per-instance body VERBATIM (governor read, decorated notes, SDV emission).
    private const string GovernorScript = """
param($server)
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    param($server)
    $resourcegov = $server.ResourceGovernor

    if ($resourcegov) {
        Add-Member -Force -InputObject $resourcegov -MemberType NoteProperty -Name ComputerName -value $server.ComputerName
        Add-Member -Force -InputObject $resourcegov -MemberType NoteProperty -Name InstanceName -value $server.ServiceName
        Add-Member -Force -InputObject $resourcegov -MemberType NoteProperty -Name SqlInstance -value $server.DomainInstanceName
    }

    Select-DefaultView -InputObject $resourcegov -Property ComputerName, InstanceName, SqlInstance, ClassifierFunction, Enabled, MaxOutstandingIOPerVolume, ReconfigurePending, ResourcePools, ExternalResourcePools
} $server 3>&1 2>&1
""";
}
