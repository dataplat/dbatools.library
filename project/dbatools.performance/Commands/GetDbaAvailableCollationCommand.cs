#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;
using Microsoft.SqlServer.Management.Smo;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Lists the collations an instance supports. Port of
/// public/Get-DbaAvailableCollation.ps1 (W1-056). Connect rides NestedConnect;
/// EnumCollations() runs natively OUTSIDE any try (a fault is statement-conditional and the
/// stale-null table then enumerates nothing); each DataRow is decorated with the five
/// Add-Member note properties (ETS instance members on the row identity); the CodePage and
/// Locale descriptions ride the PRIVATE Get-CodePage/Get-Language helpers module-scoped
/// with the function's own try/catch-to-null INSIDE the hop, memoized per invocation like
/// the begin-block caches (the 66577 Japanese_Unicode seed included); the final
/// Select-DefaultView over the whole DataTable rides a module hop and its output is the
/// command's output. Surface pinned by migration/baselines/Get-DbaAvailableCollation.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaAvailableCollation")]
public sealed class GetDbaAvailableCollationCommand : DbaInstanceCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public override DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public override PSCredential? SqlCredential { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // PS: the begin-block caches (66577 pre-seeded; both memoize nulls too).
    private readonly Dictionary<int, object?> _locales = new Dictionary<int, object?> { { 66577, "Japanese_Unicode" } };
    private readonly Dictionary<int, object?> _codePages = new Dictionary<int, object?>();

    private DataTable? _availableCollations;

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

            // PS: $availableCollations = $server.EnumCollations() - outside any try; a
            // fault surfaces statement-style and the variable keeps its previous value.
            try
            {
                _availableCollations = server.EnumCollations();
            }
            catch (PipelineStoppedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                StatementFault.Surface(this, ex, "Get-DbaAvailableCollation");
            }

            if (_availableCollations is not null)
            {
                foreach (DataRow collation in _availableCollations.Rows)
                {
                    PSObject wrapped = PSObject.AsPSObject(collation);
                    SetNote(wrapped, "ComputerName", Connection.SmoServerExtensions.GetComputerName(server));
                    SetNote(wrapped, "InstanceName", server.ServiceName);
                    SetNote(wrapped, "SqlInstance", Connection.SmoServerExtensions.GetDomainInstanceName(server));
                    SetNote(wrapped, "CodePageName", GetCodePageDescription(Convert.ToInt32(collation["CodePage"], System.Globalization.CultureInfo.InvariantCulture)));
                    SetNote(wrapped, "LocaleName", GetLocaleDescription(Convert.ToInt32(collation["LocaleID"], System.Globalization.CultureInfo.InvariantCulture)));
                }
            }

            // PS: Select-DefaultView -InputObject $availableCollations -Property ...
            try
            {
                foreach (PSObject? item in NestedCommand.InvokeScoped(this, SelectDefaultViewScript, _availableCollations))
                    WriteObject(item);
            }
            catch (PipelineStoppedException)
            {
                throw;
            }
            catch (RuntimeException ex)
            {
                StatementFault.Surface(this, ex, "Get-DbaAvailableCollation");
            }
        }
    }

    /// <summary>PS: Add-Member -Force NoteProperty (overwrite when present).</summary>
    private static void SetNote(PSObject target, string name, object? value)
    {
        PSPropertyInfo? existing = target.Properties[name];
        if (existing is not null)
            existing.Value = value;
        else
            target.Properties.Add(new PSNoteProperty(name, value));
    }

    /// <summary>PS: Get-LocaleDescription - cache, then (Get-Language $id).DisplayName with
    /// the catch-to-null inside the module hop.</summary>
    private object? GetLocaleDescription(int localeId)
    {
        object? cached;
        if (_locales.TryGetValue(localeId, out cached))
            return cached;
        Collection<PSObject> result = NestedCommand.InvokeScoped(this, GetLanguageScript, localeId);
        object? name = result.Count > 0 ? UnwrapTransit(result[result.Count - 1]) : null;
        _locales[localeId] = name;
        return name;
    }

    /// <summary>PS: Get-CodePageDescription - cache, then (Get-CodePage $id).EncodingName
    /// with the catch-to-null inside the module hop.</summary>
    private object? GetCodePageDescription(int codePageId)
    {
        object? cached;
        if (_codePages.TryGetValue(codePageId, out cached))
            return cached;
        Collection<PSObject> result = NestedCommand.InvokeScoped(this, GetCodePageScript, codePageId);
        object? name = result.Count > 0 ? UnwrapTransit(result[result.Count - 1]) : null;
        _codePages[codePageId] = name;
        return name;
    }

    private static object? UnwrapTransit(object? value)
    {
        if (value is PSObject psValue && psValue.BaseObject is not PSCustomObject)
            return psValue.BaseObject;
        return value;
    }

    private const string GetLanguageScript = """
param($__id)
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    param($__id)
    try {
        (Get-Language $__id).DisplayName
    } catch {
        $null
    }
} $__id 3>&1
""";

    private const string GetCodePageScript = """
param($__id)
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    param($__id)
    try {
        (Get-CodePage $__id).EncodingName
    } catch {
        $null
    }
} $__id 3>&1
""";

    private const string SelectDefaultViewScript = """
param($__collations)
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    param($__collations)
    Select-DefaultView -InputObject $__collations -Property ComputerName, InstanceName, SqlInstance, Name, CodePage, CodePageName, LocaleID, LocaleName, Description
} $__collations 3>&1
""";
}
