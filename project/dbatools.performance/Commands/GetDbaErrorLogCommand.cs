#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Globalization;
using System.Management.Automation;
using Dataplat.Dbatools.Connection;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;
using Microsoft.SqlServer.Management.Smo;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Reads SQL Server error log entries. Port of public/Get-DbaErrorLog.ps1 (W1-074).
/// Unbound -LogNumber walks 99..0 (reassigned inside the instance loop, same value);
/// each Server.ReadErrorLog(n) call is native and a fault surfaces the engine's
/// MethodInvocationException shape statement-conditionally (the number loop continues);
/// the compound filter keeps the PS shapes - a bound -Source array converts to the LHS
/// string for -ne (space-joined), -Text wraps in *wildcards* case-insensitively, and
/// bound -After/-Before datetimes are always truthy; matching rows log the "Processing
/// System.Data.DataRow" verbose, take the three Add-Member -Force notes (remove +
/// re-append, W1-060 law) and pipe through Select-DefaultView with the
/// 'ProcessInfo as Source' rename. Surface pinned by
/// migration/baselines/Get-DbaErrorLog.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaErrorLog")]
public sealed class GetDbaErrorLogCommand : DbaInstanceCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(ValueFromPipeline = true, Position = 0)]
    public override DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public override PSCredential? SqlCredential { get; set; }

    /// <summary>The error log number(s) to read (0 = current).</summary>
    [Parameter(Position = 2)]
    [ValidateRange(0, 99)]
    public int[]? LogNumber { get; set; }

    /// <summary>Filter by the ProcessInfo source column.</summary>
    [Parameter(Position = 3)]
    public object[]? Source { get; set; }

    /// <summary>Filter by log text (wrapped in wildcards).</summary>
    [Parameter(Position = 4)]
    public string? Text { get; set; }

    /// <summary>Only entries after this time.</summary>
    [Parameter(Position = 5)]
    public DateTime After { get; set; }

    /// <summary>Only entries before this time.</summary>
    [Parameter(Position = 6)]
    public DateTime Before { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        foreach (DbaInstanceParameter instance in SqlInstance ?? new DbaInstanceParameter[0])
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

            // PS: unbound -LogNumber reads all logs, new to old.
            int[] numbers;
            if (!TestBound("LogNumber"))
            {
                numbers = new int[100];
                for (int n = 0; n < 100; n++)
                    numbers[n] = 99 - n;
            }
            else
            {
                numbers = LogNumber ?? new int[0];
            }

            bool filterSource = PsTruthy(Source);
            bool filterText = !string.IsNullOrEmpty(Text);
            bool filterAfter = TestBound("After");
            bool filterBefore = TestBound("Before");
            WildcardPattern? textPattern = filterText ? WildcardPattern.Get("*" + Text + "*", WildcardOptions.IgnoreCase) : null;

            foreach (int number in numbers)
            {
                DataTable log;
                try
                {
                    log = server.ReadErrorLog(number);
                }
                catch (PipelineStoppedException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // PS: the foreach expression faults statement-conditionally with
                    // the engine's method-invocation shape; the number loop continues.
                    StatementFault.Surface(this, MethodFaultRecord(ex, "ReadErrorLog", 1));
                    continue;
                }

                foreach (DataRow row in log.Rows)
                {
                    PSObject wrapped = PSObject.AsPSObject(row);
                    object? processInfo = DotAccess(wrapped, "ProcessInfo");
                    object? rowText = DotAccess(wrapped, "Text");
                    object? logDate = DotAccess(wrapped, "LogDate");

                    if ((filterSource && !PsOps.Eq(processInfo, Source)) ||
                        (filterText && !textPattern!.IsMatch(PsText(rowText))) ||
                        (filterAfter && LanguagePrimitives.Compare(logDate, After, false, CultureInfo.InvariantCulture) < 0) ||
                        (filterBefore && LanguagePrimitives.Compare(logDate, Before, false, CultureInfo.InvariantCulture) > 0))
                    {
                        continue;
                    }

                    WriteMessage(MessageLevel.Verbose, "Processing " + PsText(wrapped));
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
                        StatementFault.Surface(this, ex, "Get-DbaErrorLog");
                    }
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

    /// <summary>The engine's method-fault record (the W1-063 shape).</summary>
    private static ErrorRecord MethodFaultRecord(Exception inner, string methodName, int argumentCount)
    {
        string message = "Exception calling \"" + methodName + "\" with \"" + argumentCount.ToString(CultureInfo.InvariantCulture) + "\" argument(s): \"" + inner.Message + "\"";
        return new ErrorRecord(new MethodInvocationException(message, inner), inner.GetType().Name, ErrorCategory.NotSpecified, null);
    }

    /// <summary>PS array truthiness: empty = false, one element = its truthiness,
    /// two or more = true.</summary>
    private static bool PsTruthy(object[]? values)
    {
        if (values is null || values.Length == 0)
            return false;
        if (values.Length == 1)
            return LanguagePrimitives.IsTrue(values[0]);
        return true;
    }

    /// <summary>The PS dot operator (raw DataRow column reads).</summary>
    private static object? DotAccess(object? item, string name)
    {
        if (item is null)
            return null;
        PSObject wrapped = PSObject.AsPSObject(item);
        PSPropertyInfo? direct = wrapped.Properties[name];
        if (direct is null)
            return null;
        object? value;
        try { value = direct.Value; }
        catch { return null; }
        if (value is PSObject psValue && psValue.BaseObject is not PSCustomObject)
            return psValue.BaseObject;
        return value;
    }

    /// <summary>PS string interpolation via LanguagePrimitives (invariant).</summary>
    private static string PsText(object? value)
    {
        if (value is null)
            return "";
        return (string)LanguagePrimitives.ConvertTo(value, typeof(string), CultureInfo.InvariantCulture);
    }

    private const string SelectDefaultViewScript = """
param($__object)
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    param($__object)
    Select-DefaultView -InputObject $__object -Property ComputerName, InstanceName, SqlInstance, LogDate, "ProcessInfo as Source", Text
} $__object 3>&1
""";
}
