#nullable enable

using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Data;
using System.Globalization;
using System.Management.Automation;
using System.Text;
using Dataplat.Dbatools.Connection;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;
using Microsoft.SqlServer.Management.Smo;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Runs DBCC INPUTBUFFER/OUTPUTBUFFER per session. Port of
/// public/Get-DbaDbccSessionBuffer.ps1 (W1-063). Quirks preserved: -Operation uppercases
/// ONLY when explicitly bound (the unbound default keeps mixed case in the query text);
/// the non-All path logs "Query to run" TWICE per session - first with the raw
/// #Operation# placeholder, then substituted (the All path logs only the substituted
/// line); the "Output Buffer" verbose line exists ONLY in the non-All path; the All path
/// ignores -RequestId; the All-path session-list Query sits OUTSIDE any try (statement
/// fault surfaces conditionally and the STALE list from a prior iteration then
/// enumerates - field-persisted); the OUTPUTBUFFER hexdump parse ($str = $row[0].ToString()
/// then fixed Substring(11,48)/(61,16) appends) fault per STATEMENT with $str persisting
/// stale; INPUTBUFFER emits plain PSCustomObjects while OUTPUTBUFFER pipes one per-session
/// object through Select-DefaultView. Surface pinned by
/// migration/baselines/Get-DbaDbccSessionBuffer.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaDbccSessionBuffer")]
public sealed class GetDbaDbccSessionBufferCommand : DbaInstanceCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public override DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public override PSCredential? SqlCredential { get; set; }

    /// <summary>Which DBCC operation to execute.</summary>
    [Parameter(Position = 2)]
    [ValidateSet("InputBuffer", "OutputBuffer")]
    public string Operation { get; set; } = "InputBuffer";

    /// <summary>The session IDs to examine.</summary>
    [Parameter(Position = 3)]
    public int[]? SessionId { get; set; }

    /// <summary>The request (batch) to examine within the session.</summary>
    [Parameter(Position = 4)]
    public int RequestId { get; set; }

    /// <summary>Return buffers for all sessions in sys.dm_exec_connections.</summary>
    [Parameter]
    public SwitchParameter All { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    private string _template = "";

    // PS: process-block locals persist across iterations; a faulted statement keeps the
    // previous value (stale-value law).
    private Collection<PSObject> _sessionList = new Collection<PSObject>();
    private string? _str;

    protected override void BeginProcessing()
    {
        if (!TestBound("All"))
        {
            if (!TestBound("SessionId"))
            {
                StopFunction("You must specify either a SessionId or use the -All switch.");
                return;
            }
        }

        // PS: the uppercase happens ONLY when -Operation was explicitly bound.
        string operation = Operation;
        if (TestBound("Operation"))
            operation = Operation.ToUpper();
        _template = "DBCC " + operation + "(#Operation#) WITH NO_INFOMSGS";
    }

    protected override void ProcessRecord()
    {
        // PS: if (Test-FunctionInterrupt) { return }
        if (Interrupted)
            return;

        // PS: $Operation -eq 'INPUTBUFFER' is an ordinal-ignore-case compare.
        bool inputBuffer = string.Equals(Operation, "INPUTBUFFER", StringComparison.OrdinalIgnoreCase);

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

            if (!TestBound("All"))
            {
                foreach (int sessionId in SessionId ?? new int[0])
                {
                    string query = _template;

                    // PS quirk: the pre-substitution template logs first in BOTH branches.
                    if (!TestBound("RequestId"))
                    {
                        WriteMessage(MessageLevel.Verbose, "Query to run: " + query);
                        query = query.Replace("#Operation#", sessionId.ToString(CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        WriteMessage(MessageLevel.Verbose, "Query to run: " + query);
                        query = query.Replace("#Operation#", sessionId.ToString(CultureInfo.InvariantCulture) + ", " + RequestId.ToString(CultureInfo.InvariantCulture));
                    }

                    Collection<PSObject> results;
                    try
                    {
                        WriteMessage(MessageLevel.Verbose, "Query to run: " + query);
                        results = NestedCommand.InvokeScoped(this, ServerQueryScript, server, query);
                    }
                    catch (PipelineStoppedException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        StopFunction("Failure", target: server, errorRecord: StatementFault.Record(ex, "Get-DbaDbccSessionBuffer"), continueLoop: true);
                        continue;
                    }

                    if (inputBuffer)
                    {
                        EmitInputBufferRows(server, results, sessionId);
                    }
                    else
                    {
                        WriteMessage(MessageLevel.Verbose, "Output Buffer");
                        EmitOutputBufferObject(server, results, sessionId);
                    }
                }
            }
            else
            {
                // PS: $sessionList = $server.Query($sessionQuery) - OUTSIDE any try; a
                // fault surfaces statement-style and the STALE list then enumerates.
                string sessionQuery = "SELECT session_id FROM sys.dm_exec_connections";
                try
                {
                    _sessionList = NestedCommand.InvokeScoped(this, ServerQueryScript, server, sessionQuery);
                }
                catch (PipelineStoppedException)
                {
                    throw;
                }
                catch (RuntimeException ex)
                {
                    StatementFault.Surface(this, ex, "Get-DbaDbccSessionBuffer");
                }

                foreach (PSObject? session in _sessionList)
                {
                    if (session is null)
                        continue;
                    object? sessionIdValue = session.BaseObject is DataRow sessionRow && sessionRow.Table.Columns.Contains("session_id") ? sessionRow["session_id"] : null;
                    string query = _template;
                    query = query.Replace("#Operation#", PsText(sessionIdValue));

                    Collection<PSObject> results;
                    try
                    {
                        WriteMessage(MessageLevel.Verbose, "Query to run: " + query);
                        results = NestedCommand.InvokeScoped(this, ServerQueryScript, server, query);
                    }
                    catch (PipelineStoppedException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        StopFunction("Failure", target: server, errorRecord: StatementFault.Record(ex, "Get-DbaDbccSessionBuffer"), continueLoop: true);
                        continue;
                    }

                    if (inputBuffer)
                    {
                        EmitInputBufferRows(server, results, sessionIdValue);
                    }
                    else
                    {
                        // PS quirk: no "Output Buffer" verbose line in the All path.
                        EmitOutputBufferObject(server, results, sessionIdValue);
                    }
                }
            }
        }
    }

    /// <summary>The INPUTBUFFER projection: one plain PSCustomObject per row.</summary>
    private void EmitInputBufferRows(Server server, Collection<PSObject> results, object? sessionId)
    {
        foreach (PSObject? item in results)
        {
            if (item?.BaseObject is not DataRow row)
                continue;
            // PS: out-of-range ordinal reads are NULL, no fault (the W1-061 law).
            int columnCount = row.Table.Columns.Count;
            PSObject result = new PSObject();
            result.Properties.Add(new PSNoteProperty("ComputerName", SmoServerExtensions.GetComputerName(server)));
            result.Properties.Add(new PSNoteProperty("InstanceName", server.ServiceName));
            result.Properties.Add(new PSNoteProperty("SqlInstance", SmoServerExtensions.GetDomainInstanceName(server)));
            result.Properties.Add(new PSNoteProperty("SessionId", sessionId));
            result.Properties.Add(new PSNoteProperty("EventType", columnCount > 0 ? row[0] : null));
            result.Properties.Add(new PSNoteProperty("Parameters", columnCount > 1 ? row[1] : null));
            result.Properties.Add(new PSNoteProperty("EventInfo", columnCount > 2 ? row[2] : null));
            WriteObject(result);
        }
    }

    /// <summary>The OUTPUTBUFFER projection: hex/ascii builders over the fixed-width dump,
    /// ONE object per session through Select-DefaultView. Each parse statement faults
    /// conditionally with $str persisting stale (the PS statement-fault shape).</summary>
    private void EmitOutputBufferObject(Server server, Collection<PSObject> results, object? sessionId)
    {
        StringBuilder hexStringBuilder = new StringBuilder();
        StringBuilder asciiStringBuilder = new StringBuilder();

        foreach (PSObject? item in results)
        {
            if (item?.BaseObject is not DataRow row)
                continue;
            int columnCount = row.Table.Columns.Count;
            object? cell = columnCount > 0 ? row[0] : null;
            // PS: $str = $row[0].ToString() - a null read faults InvokeMethodOnNull and
            // $str keeps its previous value; execution continues at the next statement.
            if (cell is null)
                StatementFault.Surface(this, NullMethodRecord());
            else
                _str = cell.ToString();
            if (_str is null)
            {
                StatementFault.Surface(this, NullMethodRecord());
            }
            else
            {
                try
                {
                    hexStringBuilder.Append(_str.Substring(11, 48));
                }
                catch (PipelineStoppedException) { throw; }
                catch (Exception ex) { StatementFault.Surface(this, ex, "Get-DbaDbccSessionBuffer"); }
            }
            if (_str is null)
            {
                StatementFault.Surface(this, NullMethodRecord());
            }
            else
            {
                try
                {
                    asciiStringBuilder.Append(_str.Substring(61, 16));
                }
                catch (PipelineStoppedException) { throw; }
                catch (Exception ex) { StatementFault.Surface(this, ex, "Get-DbaDbccSessionBuffer"); }
            }
        }

        PSObject result = new PSObject();
        result.Properties.Add(new PSNoteProperty("ComputerName", SmoServerExtensions.GetComputerName(server)));
        result.Properties.Add(new PSNoteProperty("InstanceName", server.ServiceName));
        result.Properties.Add(new PSNoteProperty("SqlInstance", SmoServerExtensions.GetDomainInstanceName(server)));
        result.Properties.Add(new PSNoteProperty("SessionId", sessionId));
        result.Properties.Add(new PSNoteProperty("Buffer", asciiStringBuilder.ToString().Replace(".", "").TrimEnd()));
        result.Properties.Add(new PSNoteProperty("HexBuffer", hexStringBuilder.ToString().Replace(" ", "")));

        try
        {
            foreach (PSObject? item in NestedCommand.InvokeScoped(this, SelectDefaultViewScript, result))
                WriteObject(item);
        }
        catch (PipelineStoppedException)
        {
            throw;
        }
        catch (RuntimeException ex)
        {
            StatementFault.Surface(this, ex, "Get-DbaDbccSessionBuffer");
        }
    }

    /// <summary>The engine's InvokeMethodOnNull statement-fault record.</summary>
    private static ErrorRecord NullMethodRecord()
    {
        return new ErrorRecord(new RuntimeException("You cannot call a method on a null-valued expression."), "InvokeMethodOnNull", ErrorCategory.InvalidOperation, null);
    }

    /// <summary>PS string interpolation via LanguagePrimitives (invariant).</summary>
    private static string PsText(object? value)
    {
        if (value is null)
            return "";
        return (string)LanguagePrimitives.ConvertTo(value, typeof(string), CultureInfo.InvariantCulture);
    }

    // PS: $server.Query($query) on the engine (the W1-046 seam).
    private const string ServerQueryScript = """
param($server, $query)
$server.Query($query)
""";

    private const string SelectDefaultViewScript = """
param($__buffer)
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    param($__buffer)
    Select-DefaultView -InputObject $__buffer -Property ComputerName, InstanceName, SqlInstance, SessionId, Buffer
} $__buffer 3>&1
""";
}
