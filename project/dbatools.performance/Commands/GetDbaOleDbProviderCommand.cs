#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Management.Automation;
using Dataplat.Dbatools.Connection;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;
using Microsoft.SqlServer.Management.Smo;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Lists OLE DB providers registered on the instance. Port of
/// public/Get-DbaOleDbProvider.ps1 (W1-085). Quirks preserved: the verbose message
/// interpolates the UNDEFINED $servername (module-scope dynamic read - the W1-051 law);
/// the VALUE-truthy -Provider filter does Name -in; each provider takes the three
/// Add-Member -Force notes (remove + re-append, W1-060 law) on the SMO object itself
/// and rides the per-item Select-DefaultView hop with the 12-property list; the cmdlet
/// keeps the "Default" default parameter set name. Surface pinned by
/// migration/baselines/Get-DbaOleDbProvider.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaOleDbProvider", DefaultParameterSetName = "Default")]
public sealed class GetDbaOleDbProviderCommand : DbaInstanceCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public override DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public override PSCredential? SqlCredential { get; set; }

    /// <summary>The provider name(s) to include.</summary>
    [Parameter(Position = 2)]
    public string[]? Provider { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        foreach (DbaInstanceParameter instance in SqlInstance)
        {
            Hashtable connectParams = new Hashtable();
            connectParams["SqlInstance"] = instance;
            connectParams["SqlCredential"] = SqlCredential;
            NestedConnect.Outcome connection = NestedConnect.Connect(this, connectParams);
            if (!connection.Ok)
            {
                StopFunction("Failure", target: instance, errorRecord: connection.Failure, category: ErrorCategory.ConnectionError, continueLoop: true);
                continue;
            }
            Server server = connection.Server!;

            // PS: "Getting startup procedures for $servername" - UNDEFINED variable,
            // module-scope dynamic read (the W1-051 law).
            WriteMessage(MessageLevel.Verbose, "Getting startup procedures for " + PsText(PipelineValue(NestedCommand.InvokeScoped(this, ModuleVariableScript, "servername"))));

            List<OleDbProviderSettings> providers = new List<OleDbProviderSettings>();
            bool filter = PsTruthy(Provider);
            foreach (OleDbProviderSettings candidate in server.Settings.OleDbProviderSettings)
            {
                if (filter && !MatchesAny(candidate.Name, Provider!))
                    continue;
                providers.Add(candidate);
            }

            foreach (OleDbProviderSettings oleDbProvider in providers)
            {
                PSObject wrapped = PSObject.AsPSObject(oleDbProvider);
                SetNote(wrapped, "ComputerName", SmoServerExtensions.GetComputerName(server));
                SetNote(wrapped, "InstanceName", server.ServiceName);
                SetNote(wrapped, "SqlInstance", SmoServerExtensions.GetDomainInstanceName(server));

                try
                {
                    foreach (PSObject? item in NestedCommand.InvokeScoped(this, SelectDefaultViewScript, wrapped))
                        WriteObject(item);
                }
                catch (PipelineStoppedException)
                {
                    throw;
                }
                catch (RuntimeException ex)
                {
                    StatementFault.Surface(this, ex, "Get-DbaOleDbProvider");
                }
            }
        }
    }

    /// <summary>Add-Member -Force NoteProperty: remove + re-append at the END (W1-060 law).</summary>
    private static void SetNote(PSObject target, string name, object? value)
    {
        PSPropertyInfo? existing = target.Properties[name];
        if (existing is not null && existing.IsInstance)
            target.Properties.Remove(name);
        target.Properties.Add(new PSNoteProperty(name, value));
    }

    /// <summary>PS array truthiness: empty = false, one element = its truthiness,
    /// two or more = true.</summary>
    private static bool PsTruthy(string[]? values)
    {
        if (values is null || values.Length == 0)
            return false;
        if (values.Length == 1)
            return LanguagePrimitives.IsTrue(values[0]);
        return true;
    }

    /// <summary>PS -in over the filter array (elementwise -eq).</summary>
    private static bool MatchesAny(string name, string[] values)
    {
        foreach (string value in values)
        {
            if (PsOps.Eq(name, value))
                return true;
        }
        return false;
    }

    /// <summary>PS pipeline-assignment collapse: none = null, one = the item, many = array.</summary>
    private static object? PipelineValue(Collection<PSObject> results)
    {
        if (results.Count == 0)
            return null;
        if (results.Count == 1)
            return results[0];
        object?[] array = new object?[results.Count];
        for (int n = 0; n < results.Count; n++)
            array[n] = results[n];
        return array;
    }

    /// <summary>PS string interpolation via LanguagePrimitives (invariant).</summary>
    private static string PsText(object? value)
    {
        if (value is null)
            return "";
        return (string)LanguagePrimitives.ConvertTo(value, typeof(string), CultureInfo.InvariantCulture);
    }

    // PS: the undefined fn-variable read resolves module -> global (the W1-051 law).
    private const string ModuleVariableScript = """
param($__name)
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    param($__name)
    $ExecutionContext.SessionState.PSVariable.GetValue($__name)
} $__name 3>&1
""";

    private const string SelectDefaultViewScript = """
param($__provider)
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    param($__provider)
    Select-DefaultView -InputObject $__provider -Property 'ComputerName', 'InstanceName', 'SqlInstance', 'Name', 'Description', 'AllowInProcess', 'DisallowAdHocAccess', 'DynamicParameters', 'IndexAsAccessPath', 'LevelZeroOnly', 'NestedQueries', 'NonTransactedUpdates'
} $__provider 3>&1
""";
}
