#nullable enable

using System;
using System.Management.Automation;
using Dataplat.Dbatools.Connection;
using Dataplat.Dbatools.Parameter;
using Dataplat.Dbatools.Utility;
using Microsoft.SqlServer.Management.Smo;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves SQL Server instance properties from the Information, UserOptions and Settings collections.
/// Port of public/Get-DbaInstanceProperty.ps1; surface pinned by migration/baselines/Get-DbaInstanceProperty.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaInstanceProperty", DefaultParameterSetName = "Default")]
[OutputType(typeof(PSObject))]
public sealed class GetDbaInstancePropertyCommand : DbaInstanceCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public override DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public override PSCredential? SqlCredential { get; set; }

    /// <summary>Returns only the specified property names.</summary>
    [Parameter(Position = 2)]
    public object[]? InstanceProperty { get; set; }

    /// <summary>Excludes the specified property names.</summary>
    [Parameter(Position = 3)]
    public object[]? ExcludeInstanceProperty { get; set; }

    protected override void ProcessRecord()
    {
        if (Interrupted)
        {
            return;
        }

        foreach (DbaInstanceParameter instance in SqlInstance)
        {
            // PS: try { Connect-DbaInstance } catch { Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue }
            Server? server = ConnectInstance(instance, "Failure");
            if (server is null)
            {
                continue;
            }

            try
            {
                EmitProperties(server.Information.Properties, server, "Information");
            }
            catch (PipelineStoppedException)
            {
                // A downstream Select-Object -First stopping the pipeline is engine flow
                // control, never a gathering failure; it must propagate untouched.
                throw;
            }
            catch (Exception ex)
            {
                StopFunction(string.Format("Issue gathering information properties for {0}.", instance), target: instance, errorRecord: new ErrorRecord(ex, "dbatools_Get-DbaInstanceProperty", ErrorCategory.NotSpecified, instance), continueLoop: true);
                continue;
            }

            try
            {
                EmitProperties(server.UserOptions.Properties, server, "UserOption");
            }
            catch (PipelineStoppedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                StopFunction(string.Format("Issue gathering user options for {0}.", instance), target: instance, errorRecord: new ErrorRecord(ex, "dbatools_Get-DbaInstanceProperty", ErrorCategory.NotSpecified, instance), continueLoop: true);
                continue;
            }

            try
            {
                EmitProperties(server.Settings.Properties, server, "Setting");
            }
            catch (PipelineStoppedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                StopFunction(string.Format("Issue gathering settings for {0}.", instance), target: instance, errorRecord: new ErrorRecord(ex, "dbatools_Get-DbaInstanceProperty", ErrorCategory.NotSpecified, instance), continueLoop: true);
                continue;
            }
        }
    }

    private void EmitProperties(PropertyCollection properties, Server server, string propertyType)
    {
        foreach (Property prop in properties)
        {
            // PS: Where-Object Name -In $InstanceProperty (case-insensitive) when specified
            if (FilterHelper.IsActive(InstanceProperty) && !NameMatches(prop.Name, InstanceProperty!))
            {
                continue;
            }
            if (FilterHelper.IsActive(ExcludeInstanceProperty) && NameMatches(prop.Name, ExcludeInstanceProperty!))
            {
                continue;
            }

            PSObject wrapped = PSObject.AsPSObject(prop);
            ReplaceNoteProperty(wrapped, "ComputerName", SmoServerExtensions.GetComputerName(server));
            ReplaceNoteProperty(wrapped, "InstanceName", server.ServiceName);
            ReplaceNoteProperty(wrapped, "SqlInstance", SmoServerExtensions.GetDomainInstanceName(server));
            ReplaceNoteProperty(wrapped, "PropertyType", propertyType);

            // PS: Select-DefaultView -Property ComputerName, InstanceName, SqlInstance, Name, Value, PropertyType
            OutputHelper.SetDefaultDisplayPropertySet(wrapped,
                "ComputerName", "InstanceName", "SqlInstance", "Name", "Value", "PropertyType");

            WriteObject(wrapped);
        }
    }

    private static bool NameMatches(string name, object[] filters)
    {
        foreach (object filter in filters)
        {
            if (filter is null)
            {
                continue;
            }
            if (string.Equals(name, filter.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static void ReplaceNoteProperty(PSObject wrapped, string name, object? value)
    {
        PSPropertyInfo? existing = wrapped.Properties[name];
        if (existing is PSNoteProperty)
        {
            wrapped.Properties.Remove(name);
        }
        wrapped.Properties.Add(new PSNoteProperty(name, value));
    }
}
