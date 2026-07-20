#nullable enable

using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Management.Automation;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Clears all SqlClient connection pools on the target computer(s). Port of
/// public/Clear-DbaConnectionPool.ps1 (W1-045). The four call shapes the function used
/// (remote/local x with/without -Credential, chosen by $computer.IsLocalhost and
/// Test-Bound 'Credential') run VERBATIM through the module-scoped Invoke-Command2 hop,
/// each branch carrying the function's exact { [Microsoft.Data.SqlClient.SqlConnection]::
/// ClearAllPools() } scriptblock literal. A null ComputerName element reads a null
/// IsLocalhost (falsy) and takes the remote branch into the real helper's binder, exactly
/// like the function. Hop output (none in practice - ClearAllPools is void) streams to the
/// pipeline; warnings merge back for caller -WarningVariable parity; a bound -Verbose is
/// re-established inside the hop scope (the function-local preference reach, W1-044
/// carrier). The catch maps to Stop-Function "Failure" -ErrorRecord -Target -Continue.
/// Ledger-class: the FQID command-identity suffix of engine-composed records (W5-038 kin).
/// Surface pinned by migration/baselines/Clear-DbaConnectionPool.json.
/// </summary>
[Cmdlet(VerbsCommon.Clear, "DbaConnectionPool")]
public sealed class ClearDbaConnectionPoolCommand : DbaBaseCmdlet
{
    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    [Parameter(ValueFromPipeline = true, Position = 0)]
    [Alias("cn", "host", "Server")]
    public DbaInstanceParameter[]? ComputerName { get; set; }

    [Parameter(Position = 1)]
    public PSCredential? Credential { get; set; }

    protected override void BeginProcessing()
    {
        // PS: [DbaInstanceParameter[]]$ComputerName = $env:COMPUTERNAME (bind-time cast; a
        // null environment value casts to null and the process loop just never runs).
        if (!TestBound("ComputerName"))
        {
            string? localName = Environment.GetEnvironmentVariable("COMPUTERNAME");
            if (localName is not null)
                ComputerName = (DbaInstanceParameter[])LanguagePrimitives.ConvertTo(localName, typeof(DbaInstanceParameter[]), CultureInfo.InvariantCulture);
        }
    }

    protected override void ProcessRecord()
    {
        if (ComputerName is null)
            return;

        foreach (DbaInstanceParameter? computer in ComputerName)
        {
            try
            {
                // PS: if (-not $computer.IsLocalhost) - a null element reads null (falsy).
                bool isLocalhost = computer is not null && computer.IsLocalHost;
                string shape;
                if (!isLocalhost)
                {
                    WriteMessage(MessageLevel.Verbose, "Clearing all pools on remote computer " + PsText(computer));
                    shape = TestBound("Credential") ? "RemoteCred" : "Remote";
                }
                else
                {
                    WriteMessage(MessageLevel.Verbose, "Clearing all local pools");
                    shape = TestBound("Credential") ? "LocalCred" : "Local";
                }

                // The function's try{} sets the engine's propagate flag, so statement
                // faults at ANY depth under Invoke-Command2 unwind to this catch instead
                // of writing-and-continuing - EngineTryScope reproduces exactly that.
                using EngineTryScope tryScope = EngineTryScope.Enter(this);
                foreach (PSObject item in NestedCommand.InvokeScoped(this, InvokeCommand2Script, computer, Credential, shape, BoundVerbose(), BoundDebug()))
                {
                    // The hop merges the error stream back (2>&1): a NON-terminating error
                    // from the real Invoke-Command2 (e.g. the remote session's TypeNotFound
                    // for Microsoft.Data.SqlClient) must re-emit through THIS cmdlet's error
                    // stream - InvokeScript displays nothing itself, though the nested
                    // pipeline already bagged the record in $error, so the silent duplicate
                    // is removed before the visible re-emit (the W1-044 compensation).
                    if (item?.BaseObject is ErrorRecord nestedError)
                    {
                        RemoveHopErrorBookkeeping(nestedError);
                        WriteError(nestedError);
                    }
                    else
                    {
                        WriteObject(item);
                    }
                }
            }
            catch (PipelineStoppedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                StopFunction("Failure", target: computer, errorRecord: ToCaughtRecord(ex), continueLoop: true);
                continue;
            }
        }
    }

    /// <summary>PS string interpolation of a value ("$computer"); arrays space-join.</summary>
    private static string PsText(object? value)
    {
        if (value is null)
            return "";
        return PSObject.AsPSObject(value).ToString();
    }

    /// <summary>A bound -Debug reached the function's module-scoped nested calls through
    /// the function-LOCAL $DebugPreference; the hop script re-establishes it from this
    /// carrier (null = not bound - the ambient chain already matches).</summary>
    private object? BoundDebug()
    {
        object? debug;
        if (MyInvocation.BoundParameters.TryGetValue("Debug", out debug))
            return LanguagePrimitives.IsTrue(debug);
        return null;
    }

    /// <summary>A bound -Verbose reached the function's module-scoped nested calls through
    /// the function-LOCAL $VerbosePreference; the hop script re-establishes it from this
    /// carrier (null = not bound - the ambient chain already matches).</summary>
    private object? BoundVerbose()
    {
        object? verbose;
        if (MyInvocation.BoundParameters.TryGetValue("Verbose", out verbose))
            return LanguagePrimitives.IsTrue(verbose);
        return null;
    }

    /// <summary>PS: catch { $_ } - a nested terminating error carries the original failing
    /// record; a flattened ParentContainsErrorRecordException record (the W1-009 class)
    /// keeps its errorId/category/target but re-wraps the real exception.</summary>
    private static ErrorRecord ToCaughtRecord(Exception ex)
    {
        ErrorRecord? inner = (ex as IContainsErrorRecord)?.ErrorRecord;
        if (inner is not null && inner.Exception is not ParentContainsErrorRecordException)
            return inner;
        if (inner is not null)
            return new ErrorRecord(ex, FirstErrorIdComponent(inner.FullyQualifiedErrorId), inner.CategoryInfo.Category, inner.TargetObject);
        return new ErrorRecord(ex, "Clear-DbaConnectionPool", ErrorCategory.NotSpecified, null);
    }

    /// <summary>Removes the silent $error copy the nested pipeline bagged for a merged-back
    /// non-terminating record, so the visible WriteError re-emit nets exactly one entry
    /// like the function world. Best-effort, like InsertGlobalError.</summary>
    private void RemoveHopErrorBookkeeping(ErrorRecord record)
    {
        try
        {
            if (SessionState.PSVariable.GetValue("Error") is not System.Collections.ArrayList errorList || errorList.Count == 0)
                return;
            if (errorList[0] is not ErrorRecord first)
                return;
            if (ReferenceEquals(first, record) || ReferenceEquals(first.Exception, record.Exception) ||
                string.Equals(first.Exception?.Message, record.Exception?.Message, StringComparison.Ordinal))
            {
                errorList.RemoveAt(0);
            }
        }
        catch
        {
            // $error compensation is best-effort: constrained runspaces may deny access.
        }
    }

    private static string FirstErrorIdComponent(string? fullyQualifiedErrorId)
    {
        if (string.IsNullOrEmpty(fullyQualifiedErrorId))
            return "Clear-DbaConnectionPool";
        int comma = fullyQualifiedErrorId!.IndexOf(',');
        return comma < 0 ? fullyQualifiedErrorId : fullyQualifiedErrorId.Substring(0, comma);
    }

    // Each branch carries the function's scriptblock VERBATIM; the call shapes replicate
    // the function's four distinct Invoke-Command2 parameter bindings exactly.
    private const string InvokeCommand2Script = """
param($computer, $credential, $__shape, $__boundVerbose, $__boundDebug)
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    param($computer, $credential, $__shape, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundVerbose) { $VerbosePreference = $(if ($__boundVerbose) { "Continue" } else { "SilentlyContinue" }) }
    if ($null -ne $__boundDebug) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }
    switch ($__shape) {
        "RemoteCred" { Invoke-Command2 -ComputerName $computer -Credential $credential -ScriptBlock { [Microsoft.Data.SqlClient.SqlConnection]::ClearAllPools() } 3>&1 2>&1 }
        "Remote" { Invoke-Command2 -ComputerName $computer -ScriptBlock { [Microsoft.Data.SqlClient.SqlConnection]::ClearAllPools() } 3>&1 2>&1 }
        "LocalCred" { Invoke-Command2 -Credential $credential -ScriptBlock { [Microsoft.Data.SqlClient.SqlConnection]::ClearAllPools() } 3>&1 2>&1 }
        "Local" { Invoke-Command2 -ScriptBlock { [Microsoft.Data.SqlClient.SqlConnection]::ClearAllPools() } 3>&1 2>&1 }
    }
} $computer $credential $__shape $__boundVerbose $__boundDebug
""";
}
