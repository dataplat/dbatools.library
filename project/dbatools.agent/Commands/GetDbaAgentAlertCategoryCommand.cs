#nullable enable

using System;
using System.Globalization;
using System.Management.Automation;
using Dataplat.Dbatools.Connection;
using Dataplat.Dbatools.Parameter;
using Dataplat.Dbatools.Utility;
using Microsoft.SqlServer.Management.Smo;
using Microsoft.SqlServer.Management.Smo.Agent;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves SQL Agent alert categories and their associated alert counts.
/// Port of public/Get-DbaAgentAlertCategory.ps1; surface pinned by
/// migration/baselines/Get-DbaAgentAlertCategory.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaAgentAlertCategory")]
public sealed class GetDbaAgentAlertCategoryCommand : DbaInstanceCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public override DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public override PSCredential? SqlCredential { get; set; }

    /// <summary>One or more exact alert category names to return.</summary>
    [Parameter(Position = 2)]
    public string[]? Category { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
        {
            return;
        }

        foreach (DbaInstanceParameter instance in SqlInstance)
        {
            if (Interrupted)
                return;

            Server? server = ConnectInstance(instance, "Failure");
            if (server is null)
                continue;

            AlertCategory? currentCategory = null;
            try
            {
                foreach (AlertCategory category in server.JobServer.AlertCategories)
                {
                    currentCategory = category;
                    if (Interrupted)
                        return;
                    if (TestBound(nameof(Category)) && !Contains(Category, category.Name))
                        continue;

                    int alertCount = 0;
                    foreach (Alert alert in server.JobServer.Alerts)
                    {
                        if (LanguagePrimitives.Equals(alert.CategoryName, category.Name, true,
                            CultureInfo.InvariantCulture))
                            alertCount++;
                    }

                    PSObject wrapped = PSObject.AsPSObject(category);
                    AddOrReplaceNote(wrapped, "ComputerName", SmoServerExtensions.GetComputerName(server));
                    AddOrReplaceNote(wrapped, "InstanceName", server.ServiceName);
                    AddOrReplaceNote(wrapped, "SqlInstance", SmoServerExtensions.GetDomainInstanceName(server));
                    AddOrReplaceNote(wrapped, "AlertCount", alertCount);
                    OutputHelper.SetDefaultDisplayPropertySet(wrapped,
                        "ComputerName", "InstanceName", "SqlInstance", "Name", "ID", "AlertCount");
                    WriteObject(wrapped);
                }
            }
            catch (PipelineStoppedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                StopFunction($"Something went wrong getting the alert category {currentCategory} on {instance}",
                    target: currentCategory,
                    errorRecord: new ErrorRecord(ex, string.Empty, ErrorCategory.NotSpecified, currentCategory),
                    continueLoop: true);
                continue;
            }
        }
    }

    private static bool Contains(string[]? values, string name)
    {
        if (values is null)
            return false;
        foreach (string? value in values)
        {
            if (LanguagePrimitives.Equals(name, value, true, CultureInfo.InvariantCulture))
                return true;
        }
        return false;
    }

    private static void AddOrReplaceNote(PSObject wrapped, string name, object? value)
    {
        if (wrapped.Properties[name] is PSNoteProperty)
            wrapped.Properties.Remove(name);
        wrapped.Properties.Add(new PSNoteProperty(name, value));
    }
}
