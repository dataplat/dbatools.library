#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using Dataplat.Dbatools.Connection;
using Dataplat.Dbatools.Utility;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Returns SQL Server instances currently cached in the dbatools connection pool. Port of
/// public/Get-DbaConnectedInstance.ps1: reads the process-wide
/// ConnectionHost.ActiveConnections registry (the P0-010c unified store the compiled
/// Connect-DbaInstance and the PS functions share), with the two Select-Object statements
/// riding the REAL engine cmdlet through NestedCommand so -First/-ExpandProperty semantics
/// (missing-property and null-value error records included) match the function exactly, and
/// Hide-ConnectionString absorbed. Surface pinned by
/// migration/baselines/Get-DbaConnectedInstance.json (parameterless, single set).
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaConnectedInstance")]
[OutputType(typeof(PSObject))]
public sealed class GetDbaConnectedInstanceCommand : DbaBaseCmdlet
{
    // PS: Select-DefaultView -Property SqlInstance, ConnectionType, ConnectionObject, Pooled
    private static readonly string[] DisplaySet = { "SqlInstance", "ConnectionType", "ConnectionObject", "Pooled" };

    protected override void ProcessRecord()
    {
        if (Interrupted)
        {
            return;
        }

        Dictionary<string, List<object>> connections = ConnectionHost.ActiveConnections;
        if (connections is null)
        {
            // PS: $null.Keys null-propagates and the foreach runs zero times (only reachable
            // by externally nulling the public static store).
            return;
        }
        foreach (string key in connections.Keys)
        {
            List<object> entry = connections[key];

            // PS: if ($connections[$key].DataSource) - member-access enumeration over the
            // list, PS truthiness on the collected projection.
            object? instance;
            if (LanguagePrimitives.IsTrue(DotAccess(entry, "DataSource")))
            {
                instance = SelectFirstExpand(entry, "DataSource");
            }
            else
            {
                instance = SelectFirstExpand(entry, "Name");
            }

            object? value = SelectFirst(entry);

            // PS: if ($value.ConnectionContext.NonPooledConnection -or $value.NonPooledConnection)
            bool nonPooled = LanguagePrimitives.IsTrue(DotAccess(DotAccess(value, "ConnectionContext"), "NonPooledConnection"))
                || LanguagePrimitives.IsTrue(DotAccess(value, "NonPooledConnection"));
            bool pooling = !nonPooled;

            try
            {
                // PS: [PSCustomObject]@{...} - parser-ordered literal; ConnectionType's
                // GetType() call throws method-on-null BEFORE Hide-ConnectionString runs
                // (hashtable entries evaluate in declaration order).
                PSObject output = new();
                output.Properties.Add(new PSNoteProperty("SqlInstance", instance));
                output.Properties.Add(new PSNoteProperty("ConnectionObject", entry));
                output.Properties.Add(new PSNoteProperty("ConnectionType", GetTypePs(value).FullName));
                output.Properties.Add(new PSNoteProperty("Pooled", pooling));
                output.Properties.Add(new PSNoteProperty("ConnectionString", HideConnectionString(key)));
                OutputHelper.SetDefaultDisplayPropertySet(output, DisplaySet);
                WriteObject(output);
            }
            catch (PipelineStoppedException)
            {
                throw;
            }
            catch (RuntimeException ex)
            {
                // PS: the statement-terminating method-on-null error surfaces once and the
                // foreach continues with the next key.
                WriteError(new ErrorRecord(ex, "InvokeMethodOnNull", ErrorCategory.InvalidOperation, null));
            }
        }
    }

    // PS: $list | Select-Object -First 1 -ExpandProperty <name> - rides the real engine
    // cmdlet so empty-pipe null, missing-property and null-value error records match.
    private object? SelectFirstExpand(object? list, string name)
    {
        Hashtable splatSelect = new();
        splatSelect["First"] = 1;
        splatSelect["ExpandProperty"] = name;
        Collection<PSObject> output = NestedCommand.Invoke(this, "Select-Object", splatSelect, pipelineInput: list);
        // An array-valued property MULTI-EMITS through -ExpandProperty (lab-proven both
        // editions), so the assignment takes the PS pipeline shape: empty -> null, one ->
        // the scalar, many -> object[] (codex W1-010 r1 finding 2).
        return ShapePipelineAssignment(output);
    }

    // PS: $list | Select-Object -First 1
    private object? SelectFirst(object? list)
    {
        Hashtable splatSelect = new();
        splatSelect["First"] = 1;
        Collection<PSObject> output = NestedCommand.Invoke(this, "Select-Object", splatSelect, pipelineInput: list);
        return ShapePipelineAssignment(output);
    }

    // PS pipeline-assignment shape: empty -> null, one -> the scalar, many -> object[].
    private static object? ShapePipelineAssignment(Collection<PSObject> output)
    {
        if (output.Count == 0)
        {
            return null;
        }
        if (output.Count == 1)
        {
            return output[0];
        }
        object?[] shaped = new object?[output.Count];
        for (int i = 0; i < output.Count; i++)
        {
            shaped[i] = output[i];
        }
        return shaped;
    }

    /// <summary>
    /// PS: $value.GetType() - throws the method-on-null error when the entry list was empty
    /// (property access on null is fine; the method call is not).
    /// </summary>
    private static Type GetTypePs(object? value)
    {
        if (value is null)
        {
            // Plain RuntimeException, not PSInvalidOperationException: the engine's
            // method-on-null record carries RuntimeException, and $Error[0].Exception type +
            // CategoryInfo.Reason are observable (codex W1-010 r2 finding 1).
            throw new RuntimeException("You cannot call a method on a null-valued expression.");
        }
        return value is PSObject wrapped ? wrapped.BaseObject.GetType() : value.GetType();
    }

    /// <summary>
    /// PS dot-access on a property: a real (adapted/ETS) property wins; otherwise an
    /// enumerable base gets MEMBER-ACCESS ENUMERATION (per-element property values collected
    /// into object[], null when nothing matched).
    /// </summary>
    private static object? DotAccess(object? item, string name)
    {
        if (item is null)
        {
            return null;
        }
        PSObject wrapped = PSObject.AsPSObject(item);
        PSPropertyInfo? direct = wrapped.Properties[name];
        if (direct is not null)
        {
            try
            {
                // Returned as-is (PSObject wrapper kept): chained access must still see
                // instance ETS members on a decorated value (codex W1-010 r3 finding 3).
                return direct.Value;
            }
            catch
            {
                return null;
            }
        }
        object? baseValue = wrapped.BaseObject;
        if (baseValue is not string && LanguagePrimitives.GetEnumerable(baseValue) is IEnumerable elements)
        {
            // Lab-proven both editions (codex W1-010 r1 finding 1 + r3 finding 2):
            // member-access enumeration has PIPELINE-WRITE semantics per element - a found
            // value that is enumerable (non-string) flattens ONE level, an empty array
            // contributes nothing, a null value contributes null; a property-bag element
            // MISSING the property contributes null (permissive bag adapter) while a raw
            // .NET element missing it contributes nothing.
            List<object?> collected = new List<object?>();
            foreach (object? element in elements)
            {
                if (element is null)
                {
                    continue;
                }
                PSObject elementWrapped = PSObject.AsPSObject(element);
                PSPropertyInfo? elementProperty = elementWrapped.Properties[name];
                if (elementProperty is not null)
                {
                    object? elementValue;
                    try
                    {
                        elementValue = elementProperty.Value;
                    }
                    catch
                    {
                        collected.Add(null);
                        continue;
                    }
                    object? elementBase = elementValue is PSObject valueWrapped ? valueWrapped.BaseObject : elementValue;
                    if (elementBase is not string && LanguagePrimitives.GetEnumerable(elementBase) is IEnumerable valueItems)
                    {
                        foreach (object? valueItem in valueItems)
                        {
                            collected.Add(valueItem);
                        }
                    }
                    else
                    {
                        collected.Add(elementValue);
                    }
                }
                else if (elementWrapped.BaseObject is PSCustomObject)
                {
                    collected.Add(null);
                }
            }
            if (collected.Count == 0)
            {
                return null;
            }
            return collected.ToArray();
        }
        return null;
    }

    // Absorbed private/functions/utility/Hide-ConnectionString.ps1: mask the password when
    // present, or the fixed failure string when the key does not parse as a connection string.
    private static string HideConnectionString(string connectionString)
    {
        try
        {
            Microsoft.Data.SqlClient.SqlConnectionStringBuilder builder = new(connectionString);
            if (!string.IsNullOrEmpty(builder.Password))
            {
                builder.Password = "********";
            }
            return builder.ConnectionString;
        }
        catch
        {
            return "Failed to mask the connection string";
        }
    }
}
