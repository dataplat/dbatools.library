#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Management.Automation;
using Dataplat.Dbatools.Connection;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Utility;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Closes active SQL Server connections and removes them from the dbatools connection cache.
/// Port of public/Disconnect-DbaInstance.ps1; surface pinned by
/// migration/baselines/Disconnect-DbaInstance.json (no OutputType attribute — the PS source
/// declares none).
/// </summary>
[Cmdlet(VerbsCommunications.Disconnect, "DbaInstance", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Low)]
public sealed class DisconnectDbaInstanceCommand : DbaBaseCmdlet
{
    /// <summary>The SQL Server connection object(s) to disconnect (SMO Server, SqlConnection, or Get-DbaConnectedInstance rows).</summary>
    [Parameter(Position = 0, ValueFromPipeline = true)]
    public PSObject?[]? InputObject { get; set; }

    private List<object?> _objects = null!;

    protected override void BeginProcessing()
    {
        _objects = new List<object?>();
    }

    protected override void ProcessRecord()
    {
        // PS: $objects += $InputObject — the uninitialized $objects grows per block; an
        // unbound/absent InputObject appends one $null element (harmless downstream).
        if (InputObject is null)
        {
            _objects.Add(null);
            return;
        }
        foreach (PSObject? element in InputObject)
        {
            _objects.Add(element);
        }
    }

    protected override void EndProcessing()
    {
        foreach (object? inputItem in _objects)
        {
            // PS: if ($object.ConnectionObject) { $servers = $object.ConnectionObject } else { $servers = $object }
            object? connectionObject = PsProperty.Get(inputItem, "ConnectionObject");
            object? servers = PsOps.IsTrue(connectionObject) ? connectionObject : inputItem;

            foreach (object? server in EnumeratePs(servers))
            {
                try
                {
                    // PS: if ($server.ConnectionContext) — SMO Server (and friends) branch.
                    object? connectionContext = PsProperty.Get(server, "ConnectionContext");
                    if (PsOps.IsTrue(connectionContext))
                    {
                        string serverName = PsText(PsProperty.Get(server, "Name"));
                        if (ShouldProcess(serverName, "Disconnecting SQL Connection"))
                        {
                            // PS: $null = $server.ConnectionContext.Disconnect() — dynamic
                            // method invocation on whatever the property holds.
                            InvokePsMethod(connectionContext!, "Disconnect");

                            string? connectionString = PsProperty.Get(connectionContext, "ConnectionString") as string;
                            RemoveFromConnectionHash(connectionString);

                            PSObject result = new PSObject();
                            result.Properties.Add(new PSNoteProperty("SqlInstance", PsProperty.Get(server, "Name")));
                            result.Properties.Add(new PSNoteProperty("ConnectionString", ConnectionService.HideConnectionString(connectionString)));
                            result.Properties.Add(new PSNoteProperty("ConnectionType", BaseTypeOf(server)?.FullName));
                            result.Properties.Add(new PSNoteProperty("State", "Disconnected"));
                            OutputHelper.SetDefaultDisplayPropertySet(result, "SqlInstance", "ConnectionType", "State");
                            WriteObject(result);
                        }
                    }

                    // PS: if ($server.GetType().Name -eq "SqlConnection") — NAME match, so both
                    // System.Data.SqlClient and Microsoft.Data.SqlClient connections qualify.
                    Type? serverType = BaseTypeOf(server);
                    if (serverType is not null && PsString.Eq(serverType.Name, "SqlConnection"))
                    {
                        string dataSource = PsText(PsProperty.Get(server, "DataSource"));
                        if (ShouldProcess(dataSource, "Closing SQL Connection"))
                        {
                            // PS: if ($server.State -eq "Open") { $null = $server.Close() }
                            if (PsOps.Eq(PsProperty.Get(server, "State"), "Open"))
                            {
                                InvokePsMethod(server!, "Close");
                            }

                            string? connectionString = PsProperty.Get(server, "ConnectionString") as string;
                            RemoveFromConnectionHash(connectionString);

                            PSObject result = new PSObject();
                            result.Properties.Add(new PSNoteProperty("SqlInstance", PsProperty.Get(server, "DataSource")));
                            result.Properties.Add(new PSNoteProperty("ConnectionString", ConnectionService.HideConnectionString(connectionString)));
                            result.Properties.Add(new PSNoteProperty("ConnectionType", serverType.FullName));
                            // PS reads State AFTER the close, so the live enum value is emitted.
                            result.Properties.Add(new PSNoteProperty("State", PsProperty.Get(server, "State")));
                            OutputHelper.SetDefaultDisplayPropertySet(result, "SqlInstance", "ConnectionType", "State");
                            WriteObject(result);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // PS: Stop-Function -Message "Failed to disconnect $object" -ErrorRecord $PSItem -Continue
                    StopFunction($"Failed to disconnect {PsInterpolate(inputItem)}",
                        target: inputItem,
                        errorRecord: new ErrorRecord(ex, "Disconnect-DbaInstance", ErrorCategory.NotSpecified, inputItem),
                        continueLoop: true);
                    continue;
                }
            }
        }
    }

    /// <summary>PS foreach semantics: null iterates zero times, a collection enumerates, a scalar iterates once.</summary>
    private static IEnumerable<object?> EnumeratePs(object? value)
    {
        if (value is null)
        {
            yield break;
        }
        object baseValue = value is PSObject wrapped ? wrapped.BaseObject : value;
        if (baseValue is not string && LanguagePrimitives.GetEnumerable(baseValue) is IEnumerable elements)
        {
            foreach (object? element in elements)
            {
                yield return element;
            }
            yield break;
        }
        yield return value;
    }

    /// <summary>PS dynamic method invocation (member binder resolution over the PSObject).</summary>
    private static void InvokePsMethod(object target, string methodName)
    {
        PSMethodInfo? method = PSObject.AsPSObject(target).Methods[methodName];
        if (method is null)
        {
            // PS: calling a missing method is a statement-terminating error the enclosing
            // catch turns into the Stop-Function warning; the message names the BASE type
            // (a property bag reads as PSCustomObject, exactly like the PS binder text).
            object baseTarget = target is PSObject wrapped ? wrapped.BaseObject : target;
            throw new PSInvalidOperationException($"Method invocation failed because [{baseTarget.GetType().FullName}] does not contain a method named '{methodName}'.");
        }
        method.Invoke();
    }

    /// <summary>
    /// PS: if ([Dataplat.Dbatools.Connection.ConnectionHost]::ActiveConnections[$connString]) {
    ///     remove } — a missing key reads $null (falsy); a present-but-empty list is falsy too
    /// and is left in place.
    /// </summary>
    private void RemoveFromConnectionHash(string? connectionString)
    {
        if (connectionString is null)
        {
            // PS: a null indexer key throws (statement-terminating into the catch); the
            // realistic connections always carry a string.
            return;
        }
        if (ConnectionHost.ActiveConnections.TryGetValue(connectionString, out List<object>? registered) && PsOps.IsTrue(registered))
        {
            WriteMessage(MessageLevel.Verbose, "removing from connection hash");
            ConnectionHost.ActiveConnections.Remove(connectionString);
        }
    }

    private static Type? BaseTypeOf(object? value)
    {
        if (value is null)
        {
            return null;
        }
        return value is PSObject wrapped ? wrapped.BaseObject.GetType() : value.GetType();
    }

    /// <summary>PS expandable-string rendering of a value (null becomes empty).</summary>
    private static string PsText(object? value)
    {
        if (value is null)
        {
            return string.Empty;
        }
        return PSObject.AsPSObject(value).ToString();
    }

    /// <summary>PS "$object" interpolation for the Stop-Function message.</summary>
    private static string PsInterpolate(object? value)
    {
        return PsText(value);
    }
}
