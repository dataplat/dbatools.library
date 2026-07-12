#nullable enable

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Globalization;
using System.Management.Automation;
using System.Text.RegularExpressions;
using System.Threading;
using Dataplat.Dbatools.Message;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Management.Common;

namespace Dataplat.Dbatools.Commands;

public sealed partial class InvokeDbaQueryCommand
{
    private static readonly Regex GoSplitterRegex = new("(?smi)^[\\s]*GO[\\s]*$");

    /// <summary>
    /// The absorbed private/functions/Invoke-DbaAsync.ps1 engine: GO-splits the query, binds
    /// SqlParameters, fills a DataSet per piece (streaming PRINT/RAISERROR messages when
    /// requested) and emits in the requested -As shape. queryOverride carries the -Query
    /// value of the File/SMO call sites; null means the bound -Query (splat) value.
    /// </summary>
    private void RunAsync(ServerConnection sqlConnection, string? queryOverride)
    {
        // PS: if (Test-Bound -Not -ParameterName "QueryTimeout") { $QueryTimeout = $SqlConnection.StatementTimeout }
        // (the splat forwards QueryTimeout only when bound)
        int queryTimeout = TestBound("QueryTimeout") ? QueryTimeout : sqlConnection.StatementTimeout;
        string query = queryOverride ?? Query ?? "";

        // $SqlConnection.SqlConnectionObject is a no longer a System.Data connection, woo!
        SqlConnection conn = (SqlConnection)sqlConnection.SqlConnectionObject;

        WriteMessage(MessageLevel.Debug, "Stripping GOs from source");
        string[] rawPieces = GoSplitterRegex.Split(query);

        // Only execute non-empty statements
        List<string> pieces = new();
        foreach (string rawPiece in rawPieces)
        {
            if (rawPiece.Trim().Length > 0)
                pieces.Add(rawPiece);
        }

        foreach (string piece in pieces)
        {
            string runningStatement = piece;
            if (QuotedIdentifier.IsPresent)
                runningStatement = "SET QUOTED_IDENTIFIER ON; " + runningStatement;
            if (NoExec.IsPresent)
                runningStatement = "SET NOEXEC ON; " + runningStatement + " ;SET NOEXEC OFF;";

            SqlCommand cmd = new();
            cmd.CommandText = runningStatement;
            cmd.Connection = conn;
            cmd.CommandType = CommandType;
            cmd.CommandTimeout = queryTimeout;

            if (SqlParameter is not null)
                BindSqlParameters(cmd);

            DataSet ds = new();
            SqlDataAdapter da = new(cmd);

            if (MessagesToOutput.IsPresent)
            {
                FillStreamingMessages(conn, da, ds);
            }
            else
            {
                //Following EventHandler is used for PRINT and RAISERROR T-SQL statements. Executed when -Verbose parameter specified by caller and no -MessageToOutput
                SqlInfoMessageEventHandler? handler = null;
                if (_verboseRequested)
                {
                    conn.FireInfoMessageEventOnUserErrors = false;
                    handler = (sender, e) => WriteVerbose(e.ToString());
                    conn.InfoMessage += handler;
                }
                Exception? err = null;
                try
                {
                    da.Fill(ds);
                }
                catch (Exception ex)
                {
                    // PS: [void]$da.fill($ds) is a METHOD invocation - the caught $_ carries
                    // a MethodInvocationException wrapping the provider exception, so
                    // Resolve-SqlError's SqlException branch never matches (codex r2 F2).
                    err = WrapAsMethodInvocation(ex, "Fill", 1);
                }
                finally
                {
                    if (_verboseRequested)
                        conn.InfoMessage -= handler;
                }
                ResolveSqlError(err);
            }

            if (AppendServerInstance.IsPresent)
            {
                //Basics from Chad Miller
                DataColumn column = new();
                column.ColumnName = "ServerInstance";

                if (ds.Tables.Count != 0)
                {
                    ds.Tables[0].Columns.Add(column);
                    foreach (DataRow row in ds.Tables[0].Rows)
                        row["ServerInstance"] = sqlConnection.ServerInstance;
                }
            }

            cmd.Parameters.Clear();

            EmitResults(ds);
        }
    }

    /// <summary>The Invoke-DbaAsync SqlParameter binding block: a SqlParameter array adds
    /// each; a single dictionary adds per entry with the PS null/nested-parameter rules.</summary>
    private void BindSqlParameters(SqlCommand cmd)
    {
        object? first = SqlParameter!.Length > 0 ? (object?)SqlParameter[0] : null;
        object? firstBase = first is PSObject wrappedFirst ? wrappedFirst.BaseObject : first;
        if (firstBase is Microsoft.Data.SqlClient.SqlParameter)
        {
            foreach (PSObject sqlparam in SqlParameter)
            {
                // PS: $cmd.Parameters.Add($sqlparam) - validation deliberately checks only
                // the FIRST element, so a later non-parameter element must reach the
                // collection's own type check (its InvalidCastException text), not a C#
                // cast (codex r2 F3). The object overload matches the PS binder's pick.
                cmd.Parameters.Add((object)sqlparam.BaseObject);
            }
        }
        else
        {
            // PS: ($SqlParameter | Select-Object -First 1).GetEnumerator() on an EMPTY
            // array is a method call on $null (codex r1 F3).
            if (firstBase is not IDictionary dictionary)
                throw new RuntimeException("You cannot call a method on a null-valued expression.");
            foreach (DictionaryEntry entry in dictionary)
            {
                object? entryValue = entry.Value is PSObject wrappedValue ? wrappedValue.BaseObject : entry.Value;
                string entryKey = (string)LanguagePrimitives.ConvertTo(entry.Key, typeof(string), CultureInfo.InvariantCulture);
                if (entryValue is not null)
                {
                    if (entryValue is Microsoft.Data.SqlClient.SqlParameter nestedParameter)
                    {
                        // PS: if ($_.Value.ParameterName -ne $_.Key) - case-insensitive, so a
                        // case-only difference keeps the parameter's own name.
                        if (!PsString.Eq(nestedParameter.ParameterName, entryKey))
                            nestedParameter.ParameterName = entryKey;
                        cmd.Parameters.Add(nestedParameter);
                    }
                    else
                    {
                        cmd.Parameters.AddWithValue(entryKey, entryValue);
                    }
                }
                else
                {
                    cmd.Parameters.AddWithValue(entryKey, DBNull.Value);
                }
            }
        }
    }

    /// <summary>
    /// The -MessagesToOutput branch: the PS original fills on a runspace-pool worker while
    /// the pipeline thread drains a ConcurrentQueue of InfoMessage texts to the OUTPUT
    /// stream; a plain worker thread reproduces the streaming semantics.
    /// </summary>
    private void FillStreamingMessages(SqlConnection conn, SqlDataAdapter da, DataSet ds)
    {
        ConcurrentQueue<string> queue = new();
        Exception? fillError = null;
        Thread worker = new(() =>
        {
            conn.FireInfoMessageEventOnUserErrors = false;
            SqlInfoMessageEventHandler handler = (sender, e) => queue.Enqueue(e.ToString());
            conn.InfoMessage += handler;
            try
            {
                da.Fill(ds);
            }
            catch (Exception ex)
            {
                // Same MethodInvocationException wrap as the non-streaming path: the PS
                // runspace worker's catch also observed the method-invocation record.
                fillError = WrapAsMethodInvocation(ex, "Fill", 1);
            }
            finally
            {
                conn.InfoMessage -= handler;
            }
        });
        worker.IsBackground = true;
        worker.Start();
        // While streaming ... (the PS loop busy-polls the queue exactly like this)
        while (worker.IsAlive)
        {
            if (queue.TryDequeue(out string? item))
                WriteObject(item);
        }
        worker.Join();
        // Drain the stream as the runspace is closed, just to be safe
        while (queue.TryDequeue(out string? item))
            WriteObject(item);
        ResolveSqlError(fillError);
    }

    /// <summary>
    /// Rebuilds the MethodInvocationException the PS method binder raised for a failed
    /// .NET call: 'Exception calling "Name" with "N" argument(s): "inner"' (the reflected
    /// member name, casing-independent of the call site - lab probe-miecase, both editions).
    /// </summary>
    private static MethodInvocationException WrapAsMethodInvocation(Exception inner, string methodName, int argumentCount)
    {
        string text = "Exception calling \"" + methodName + "\" with \"" + argumentCount + "\" argument(s): \"" + inner.Message + "\"";
        return new MethodInvocationException(text, inner);
    }

    /// <summary>The absorbed Resolve-SqlError helper: swallows under
    /// SilentlyContinue/Ignore, rethrows otherwise (the caller catch turns it into the
    /// "[target] Failed during execution" Stop-Function). The SqlException branch is dead
    /// in the PS source (the caught record's exception is always the method-invocation
    /// wrapper) and stays dead here now that Fill failures arrive wrapped.</summary>
    private void ResolveSqlError(Exception? err)
    {
        if (err is null)
            return;
        if (string.Equals(err.GetType().Name, "SqlException", StringComparison.OrdinalIgnoreCase))
        {
            // For SQL exception
            WriteMessage(MessageLevel.Debug, "Capture SQL Error");
        }
        else
        {
            // For other exception
            WriteMessage(MessageLevel.Debug, "Capture Other Error");
        }
        // PS: the nested Resolve-SqlError's own $PSBoundParameters never contains Verbose,
        // so its "SQL Error:"/"Other Error:" verbose lines are DEAD CODE (codex r1 F2).
        string preference = GetEffectiveErrorActionPreference();
        bool swallow = string.Equals(preference, "SilentlyContinue", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(preference, "Ignore", StringComparison.OrdinalIgnoreCase);
        // The PS method-invocation RAISE landed one record in the caller's -ErrorVariable/
        // $error even when Resolve-SqlError swallows it (lab: evCount=1 under
        // SilentlyContinue). One WriteError reproduces that semantic core; the additional
        // frame-rethrow copies PS accumulates under Continue (lab: 12) are engine
        // bookkeeping - accepted-class, see the r2 disposition. Stop/Default skip the
        // write: WriteError would itself convert to the terminating preference exception
        // and replace the record chain the outer catch must observe.
        if (swallow || string.Equals(preference, "Continue", StringComparison.OrdinalIgnoreCase))
            WriteError(RecordFrom(err));
        if (swallow)
            return;
        throw err;
    }

    private string GetEffectiveErrorActionPreference()
    {
        object? bound;
        if (MyInvocation.BoundParameters.TryGetValue("ErrorAction", out bound) && bound is not null)
            return bound.ToString()!;
        object? preference = SessionState.PSVariable.GetValue("ErrorActionPreference");
        return preference?.ToString() ?? "Continue";
    }

    /// <summary>The embedded DBNullScrubber (props to Dave Wyatt), native.</summary>
    private static PSObject DataRowToPSObject(DataRow row)
    {
        PSObject psObject = new();

        if (row is not null && (row.RowState & DataRowState.Detached) != DataRowState.Detached)
        {
            foreach (DataColumn column in row.Table.Columns)
            {
                object? value = null;
                if (!row.IsNull(column))
                    value = row[column];

                psObject.Properties.Add(new PSNoteProperty(column.ColumnName, value));
            }
        }

        return psObject;
    }

    /// <summary>The Invoke-DbaAsync output switch over -As.</summary>
    private void EmitResults(DataSet ds)
    {
        if (string.Equals(As, "DataSet", StringComparison.OrdinalIgnoreCase))
        {
            WriteObject(ds);
        }
        else if (string.Equals(As, "DataTable", StringComparison.OrdinalIgnoreCase))
        {
            // PS emits $ds.Tables - the pipeline enumerates the collection to DataTables.
            WriteObject(ds.Tables, true);
        }
        else if (string.Equals(As, "DataRow", StringComparison.OrdinalIgnoreCase))
        {
            if (ds.Tables.Count != 0)
            {
                // PS emits $ds.Tables[0] - the pipeline enumerates a DataTable to its rows.
                WriteObject(ds.Tables[0], true);
            }
        }
        else if (string.Equals(As, "PSObject", StringComparison.OrdinalIgnoreCase))
        {
            foreach (DataTable table in ds.Tables)
            {
                //Scrub DBNulls - Provides convenient results you can use comparisons with
                foreach (DataRow row in table.Rows)
                    WriteObject(DataRowToPSObject(row));
            }
        }
        else if (string.Equals(As, "PSObjectArray", StringComparison.OrdinalIgnoreCase))
        {
            foreach (DataTable table in ds.Tables)
            {
                List<PSObject> rows = new();
                foreach (DataRow row in table.Rows)
                    rows.Add(DataRowToPSObject(row));
                // PS: , $rows - the comma-wrap emits the collected value as ONE object
                // (the single object for one row, object[] for many - the element type is
                // object[], not PSObject[], and GetType is observable). An EMPTY table
                // leaves $rows as AutomationNull, and , $rows unwraps to it in the
                // pipeline: NOTHING reaches the caller (codex r2 F1).
                object? shaped;
                if (rows.Count == 0)
                {
                    continue;
                }
                else if (rows.Count == 1)
                {
                    shaped = rows[0];
                }
                else
                {
                    object[] collected = new object[rows.Count];
                    for (int i = 0; i < rows.Count; i++)
                        collected[i] = rows[i];
                    shaped = collected;
                }
                WriteObject(shaped, false);
            }
        }
        else if (string.Equals(As, "SingleValue", StringComparison.OrdinalIgnoreCase))
        {
            if (ds.Tables.Count != 0)
            {
                // PS: $ds.Tables[0] | Select-Object -ExpandProperty <first column name> -
                // the REAL Select-Object rides nested for expansion semantics.
                Hashtable selectParams = new();
                selectParams["ExpandProperty"] = ds.Tables[0].Columns[0].ColumnName;
                Collection<PSObject> expanded = NestedCommand.Invoke(this, "Select-Object", selectParams, ds.Tables[0]);
                foreach (PSObject item in expanded)
                    WriteObject(item);
            }
        }
    }

    /// <summary>PS string interpolation of an arbitrary token.</summary>
    private static string PsText(object? value)
    {
        if (value is null)
            return "";
        return PSObject.AsPSObject(value).ToString();
    }

    /// <summary>The catch-block $_ equivalent for a .NET-thrown exception. A REAL nested
    /// record rides through; a hand-constructed RuntimeException's lazily-created record
    /// wraps itself in ParentContainsErrorRecordException and DROPS the inner-exception
    /// chain (observed: the r2 F2 MethodInvocationException wrapper leaked into the
    /// warning because the deepest-message walk lost the SqlException), so those build a
    /// fresh record from the exception itself.</summary>
    private static ErrorRecord RecordFrom(Exception ex)
    {
        if (ex is RuntimeException runtime && runtime.ErrorRecord is not null &&
            runtime.ErrorRecord.Exception is not ParentContainsErrorRecordException)
        {
            return runtime.ErrorRecord;
        }
        return new ErrorRecord(ex, "Invoke-DbaQuery", ErrorCategory.NotSpecified, null);
    }

    /// <summary>PS pipeline-assignment shaping of a nested result (null/single/array).</summary>
    private static object? ShapePipelineValue(Collection<PSObject> raw)
    {
        if (raw.Count == 0)
            return null;
        if (raw.Count == 1)
            return raw[0];
        object[] shaped = new object[raw.Count];
        for (int i = 0; i < raw.Count; i++)
            shaped[i] = raw[i];
        return shaped;
    }

    /// <summary>PS: "$(Get-DbatoolsPath -Name temp)" via the real command.</summary>
    private string GetDbatoolsTempPath()
    {
        Hashtable pathParams = new();
        pathParams["Name"] = "temp";
        Collection<PSObject> result = NestedCommand.Invoke(this, "Get-DbatoolsPath", pathParams);
        return PsText(ShapePipelineValue(result));
    }

    /// <summary>
    /// PS http-file branch: Invoke-TlsWebRequest (a PRIVATE module function, so it runs in
    /// the dbatools module scope) with the default-proxy-credentials retry; returns the
    /// failure record of the RETRY (the outer catch's $_) or null on success.
    /// </summary>
    private ErrorRecord? DownloadSqlFile(string uri, string outFile)
    {
        Hashtable requestParams = new();
        requestParams["Uri"] = uri;
        requestParams["OutFile"] = outFile;
        requestParams["ErrorAction"] = "Stop";
        try
        {
            try
            {
                ModuleScopedInvoke("Invoke-TlsWebRequest", requestParams);
            }
            catch (PipelineStoppedException)
            {
                throw;
            }
            catch
            {
                // PS: (New-Object System.Net.WebClient).Proxy.Credentials = [System.Net.CredentialCache]::DefaultNetworkCredentials
                // (mutates the shared default proxy, then retries)
#pragma warning disable SYSLIB0014
                System.Net.WebClient webClient = new();
#pragma warning restore SYSLIB0014
                // A null default proxy faults the assignment exactly like the PS statement
                // would; the outer catch owns it.
                webClient.Proxy!.Credentials = System.Net.CredentialCache.DefaultNetworkCredentials;
                ModuleScopedInvoke("Invoke-TlsWebRequest", requestParams);
            }
            return null;
        }
        catch (PipelineStoppedException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return RecordFrom(ex);
        }
    }

    /// <summary>Runs a PRIVATE dbatools function in the module scope (W5-027 pattern).</summary>
    private Collection<PSObject> ModuleScopedInvoke(string commandName, Hashtable parameters)
    {
        ScriptBlock script = ScriptBlock.Create(
            "param($__cmd, $__params) & (Get-Module dbatools | Where-Object ModuleType -eq \"Script\" | Select-Object -First 1) { param($c, $p) & $c @p } $__cmd $__params");
        return InvokeCommand.InvokeScript(true, script, null, commandName, parameters);
    }

    /// <summary>PS: Resolve-Path $item | Select-Object -ExpandProperty Path | Get-Item -ErrorAction Stop.</summary>
    private List<PSObject> ResolvePathItems(string item)
    {
        Hashtable resolveParams = new();
        resolveParams["Path"] = item;
        Collection<PSObject> resolved = NestedCommand.Invoke(this, "Resolve-Path", resolveParams);
        List<string> pathTexts = new();
        foreach (PSObject pathInfo in resolved)
            pathTexts.Add(PsText(PsProperty.Get(pathInfo, "Path")));
        List<PSObject> items = new();
        if (pathTexts.Count == 0)
            return items;
        Hashtable getItemParams = new();
        getItemParams["Path"] = pathTexts.ToArray();
        getItemParams["ErrorAction"] = "Stop";
        foreach (PSObject fileItem in NestedCommand.Invoke(this, "Get-Item", getItemParams))
            items.Add(fileItem);
        return items;
    }

    /// <summary>PS: $(Resolve-Path -LiteralPath $item).ProviderPath (interpolated downstream,
    /// so a resolution failure yields an empty string).</summary>
    private string GetLiteralProviderPath(string item)
    {
        Hashtable resolveParams = new();
        resolveParams["LiteralPath"] = item;
        Collection<PSObject> resolved = NestedCommand.Invoke(this, "Resolve-Path", resolveParams);
        object? providerPath = resolved.Count > 0 ? PsProperty.Get(resolved[0], "ProviderPath") : null;
        return PsText(providerPath);
    }

    /// <summary>
    /// Runs a nested command with PS-bubbling warning parity (same pattern as
    /// ImportDbaCsvCommand.ConnectViaCommand / ImportDbatoolsConfigCommand — third private
    /// copy; shared-promotion is an owner hand-back): the script-side catch returns the
    /// outcome as data so warnings written before a terminating failure still reach this
    /// cmdlet, which re-emits them display-suppressed for -WarningVariable capture.
    /// </summary>
    private Collection<PSObject> InvokeNestedPreservingWarnings(string commandName, Hashtable parameters, object? pipelineInput, out ErrorRecord? failure)
    {
        // PS: a bound -WarningAction on the outer command sets the preference the nested
        // call inherits (display suppression; -WarningVariable still captures).
        ScriptBlock script;
        if (pipelineInput is null)
        {
            script = ScriptBlock.Create(
                "param($__params, $__wp) if ($null -ne $__wp) { $WarningPreference = $__wp } try { $__r = & " + commandName + " @__params -WarningVariable __nestedWarnings; @{ ok = $true; result = $__r; warnings = $__nestedWarnings } } catch { @{ ok = $false; record = $_; warnings = $__nestedWarnings } }");
        }
        else
        {
            script = ScriptBlock.Create(
                "param($__params, $__wp, $__input) if ($null -ne $__wp) { $WarningPreference = $__wp } try { $__r = $__input | & " + commandName + " @__params -WarningVariable __nestedWarnings; @{ ok = $true; result = $__r; warnings = $__nestedWarnings } } catch { @{ ok = $false; record = $_; warnings = $__nestedWarnings } }");
        }
        object? boundWarningAction;
        MyInvocation.BoundParameters.TryGetValue("WarningAction", out boundWarningAction);

        Collection<PSObject> raw;
        // Empty-table shield: module-internal calls never saw caller-local OR global
        // defaults (lab-proven via the PassThru-injection probe).
        object? effectiveDefaults = SessionState.PSVariable.GetValue("PSDefaultParameterValues");
        bool shielded = effectiveDefaults is not null;
        if (shielded)
            SessionState.PSVariable.Set("PSDefaultParameterValues", new DefaultParameterDictionary());
        try
        {
            raw = pipelineInput is null
                ? InvokeCommand.InvokeScript(true, script, null, parameters, boundWarningAction)
                : InvokeCommand.InvokeScript(true, script, null, parameters, boundWarningAction, pipelineInput);
        }
        finally
        {
            if (shielded)
                SessionState.PSVariable.Set("PSDefaultParameterValues", effectiveDefaults);
        }

        Hashtable outcome = (Hashtable)raw[0].BaseObject;

        object? warnings = outcome["warnings"];
        if (warnings is not null)
        {
            IEnumerable? enumerable = LanguagePrimitives.GetEnumerable(warnings);
            if (enumerable is not null)
            {
                foreach (object? warningItem in enumerable)
                {
                    object? unwrapped = warningItem is PSObject wrappedWarning ? wrappedWarning.BaseObject : warningItem;
                    string text = unwrapped is WarningRecord warningRecord ? warningRecord.Message : PsText(unwrapped);
                    object? oldPreference = SessionState.PSVariable.GetValue("WarningPreference");
                    try
                    {
                        SessionState.PSVariable.Set("WarningPreference", ActionPreference.SilentlyContinue);
                        WriteWarning(text);
                    }
                    finally
                    {
                        SessionState.PSVariable.Set("WarningPreference", oldPreference);
                    }
                }
            }
        }

        if (!LanguagePrimitives.IsTrue(outcome["ok"]))
        {
            object? record = outcome["record"];
            if (record is PSObject wrappedRecord)
                record = wrappedRecord.BaseObject;
            failure = (ErrorRecord)record!;
            return new Collection<PSObject>();
        }

        failure = null;
        Collection<PSObject> results = new();
        object? resultValue = outcome["result"];
        if (resultValue is not null)
        {
            IEnumerable? resultEnumerable = LanguagePrimitives.GetEnumerable(resultValue);
            if (resultEnumerable is null)
            {
                results.Add(PSObject.AsPSObject(resultValue));
            }
            else
            {
                foreach (object? element in resultEnumerable)
                {
                    if (element is not null)
                        results.Add(PSObject.AsPSObject(element));
                }
            }
        }
        return results;
    }
}
