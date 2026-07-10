#nullable enable

using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves the network (TLS) certificate configured for a SQL Server instance by delegating to
/// Get-DbaNetworkConfiguration with OutputType Certificate. Port of
/// public/Get-DbaNetworkCertificate.ps1; surface pinned by
/// migration/baselines/Get-DbaNetworkCertificate.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaNetworkCertificate")]
[OutputType(typeof(PSObject))]
public sealed class GetDbaNetworkCertificateCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance(s); defaults to the local computer.</summary>
    [Parameter(ValueFromPipeline = true, Position = 0)]
    [Alias("ComputerName")]
    public DbaInstanceParameter[]? SqlInstance { get; set; } = DefaultSqlInstance();

    /// <summary>Alternate Windows credential for the target computer.</summary>
    [Parameter(Position = 1)]
    public PSCredential? Credential { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
        {
            return;
        }

        // PS: $PSBoundParameters["OutputType"] = "Certificate"
        //     Get-DbaNetworkConfiguration @PSBoundParameters | Where-Object Thumbprint
        // Forward only the BOUND parameters (so Get-DbaNetworkConfiguration applies its own
        // defaults for anything unbound, exactly like the splat did) plus OutputType.
        Hashtable splat = new()
        {
            { "OutputType", "Certificate" }
        };
        if (TestBound(nameof(SqlInstance)))
        {
            splat["SqlInstance"] = SqlInstance;
        }
        if (TestBound(nameof(Credential)))
        {
            splat["Credential"] = Credential;
        }
        if (TestBound(nameof(EnableException)))
        {
            splat["EnableException"] = EnableException;
        }

        Collection<PSObject> results = NestedCommand.Invoke(this, "Get-DbaNetworkConfiguration", splat);
        foreach (PSObject output in results)
        {
            if (output is null)
            {
                continue;
            }
            // PS: | Where-Object Thumbprint - keep objects whose Thumbprint property is truthy.
            object? thumbprint = output.Properties["Thumbprint"]?.Value;
            if (LanguagePrimitives.IsTrue(thumbprint))
            {
                WriteObject(output);
            }
        }
    }

    // PS: [DbaInstanceParameter[]]$SqlInstance = $env:COMPUTERNAME
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
