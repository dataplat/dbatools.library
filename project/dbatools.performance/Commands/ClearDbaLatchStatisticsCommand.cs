#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Message;
using Dataplat.Dbatools.Parameter;
using Dataplat.Dbatools.Utility;
using Microsoft.SqlServer.Management.Smo;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Clears sys.dm_os_latch_stats via DBCC SQLPERF. Port of
/// public/Clear-DbaLatchStatistics.ps1 (W1-046). The query rides the REAL dbatools ETS
/// ScriptMethod Server.Query (xml/dbatools.Types.ps1xml) through the object's PSObject
/// member table, so the inner-exception message walk, the Tables[0] projection, and the
/// engine's method-fault wrap are all the function's own; whatever the method RETURNS is
/// emitted to the pipeline exactly like the function's bare expression statement, and the
/// caught fault object lands in Status verbatim ($status = $_.Exception). ConfirmImpact is
/// High, so ShouldProcess rides the ShouldProcessSafe wrapper (the W5-025 non-promptable
/// host fact). Connect failure maps to Stop-Function -Category ConnectionError -Continue
/// (DbaInstanceCmdlet.ConnectInstance, MinimumVersion 9).
/// Surface pinned by migration/baselines/Clear-DbaLatchStatistics.json.
/// </summary>
[Cmdlet(VerbsCommon.Clear, "DbaLatchStatistics", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.High)]
public sealed class ClearDbaLatchStatisticsCommand : DbaInstanceCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public override DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public override PSCredential? SqlCredential { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        foreach (DbaInstanceParameter instance in SqlInstance)
        {
            // PS: Write-Message -Level Verbose -Message "Attempting to connect to $instance"
            WriteMessage(MessageLevel.Verbose, "Attempting to connect to " + PsText(instance));

            // PS: try { Connect-DbaInstance -MinimumVersion 9 } catch { Stop-Function
            //     -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue }
            Hashtable connectParams = new Hashtable();
            connectParams["SqlInstance"] = instance;
            connectParams["SqlCredential"] = SqlCredential;
            connectParams["MinimumVersion"] = 9;
            NestedConnect.Outcome connection = NestedConnect.Connect(this, connectParams);
            if (!connection.Ok)
            {
                StopFunction("Failure", target: instance, errorRecord: connection.Failure, category: ErrorCategory.ConnectionError, continueLoop: true);
                continue;
            }

            if (ShouldProcessSafe(PsText(instance), "Performing CLEAR of sys.dm_os_latch_stats"))
            {
                object status;
                try
                {
                    // PS: $server.Query(...) - the bare expression statement rides the
                    // engine VERBATIM (ETS ScriptMethod dispatch, real-$null emission, the
                    // silent inner $error bookkeeping) under the function try{}'s engine
                    // flag; every returned item streams before the result object.
                    using EngineTryScope tryScope = EngineTryScope.Enter(this);
                    foreach (PSObject? item in NestedCommand.InvokeScoped(this, ServerQueryScript, connection.ServerValue, "DBCC SQLPERF (N'sys.dm_os_latch_stats', CLEAR);"))
                        WriteObject(item);
                    status = "Success";
                }
                catch (PipelineStoppedException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // PS: $status = $_.Exception (the fault OBJECT, not its text)
                    status = ex;
                }

                PSObject result = new PSObject();
                if (connection.Server is Server server)
                {
                    OutputHelper.AddInstanceProperties(result, server);
                }
                else
                {
                    // PS: $server.ComputerName etc. on a null/foreign value read null
                    result.Properties.Add(new PSNoteProperty("ComputerName", null));
                    result.Properties.Add(new PSNoteProperty("InstanceName", null));
                    result.Properties.Add(new PSNoteProperty("SqlInstance", null));
                }
                result.Properties.Add(new PSNoteProperty("Status", status));
                WriteObject(result);
            }
        }
    }

    // PS: $server.Query($query) - the statement runs on the engine so the ETS
    // ScriptMethod, its silent inner bookkeeping, and real-$null output are the
    // function's own mechanics.
    private const string ServerQueryScript = """
param($server, $query)
$server.Query($query)
""";

    /// <summary>PS ShouldProcess under a non-promptable host (the gate's nested pwsh under
    /// Pester) faults on the confirmation prompt; proceed like the script function does in
    /// the same context (the W5-025 High-impact convention).</summary>
    private bool ShouldProcessSafe(string target, string action)
    {
        try
        {
            return ShouldProcess(target, action);
        }
        catch (PipelineStoppedException)
        {
            throw;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>PS string interpolation of a value ("$instance").</summary>
    private static string PsText(object? value)
    {
        if (value is null)
            return "";
        return PSObject.AsPSObject(value).ToString();
    }
}
