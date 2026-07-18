#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;
using SmoAgentJob = Microsoft.SqlServer.Management.Smo.Agent.Job;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves SQL Server Agent jobs that are currently executing.
/// </summary>
/// <remarks>
/// The instance connection, the JobServer refresh, the Get-DbaAgentJob call and the run-status
/// filtering all run the original dbatools PowerShell body inside the dbatools module scope rather
/// than being reimplemented in C#, so the engine keeps deciding the observable details.
///
/// Both SqlInstance and InputObject bind from the pipeline. Neither is carried across records: the
/// module scope lives for one record and both loops iterate only what the current record supplied.
///
/// Output streams as it is produced. A single record can hold several instances or several jobs (a
/// directly bound array, or multiple instances), and the body emits each running job before a later
/// instance's connection or a later job's refresh may throw; the script implementation streamed
/// those early results, so buffering them and losing them to a later terminating failure would
/// diverge.
/// </remarks>
[Cmdlet(VerbsCommon.Get, "DbaRunningJob")]
public sealed class GetDbaRunningJobCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(ValueFromPipeline = true, Position = 0)]
    [PsDbaInstanceArrayCast]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>Agent job objects supplied directly or through the pipeline.</summary>
    [Parameter(ValueFromPipeline = true, Position = 2)]
    [PsAgentJobArrayCast]
    public SmoAgentJob[]? InputObject { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
        {
            return;
        }

        NestedCommand.InvokeScopedStreaming(this, item =>
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                RemoveHopErrorBookkeeping(nestedError);
                WriteError(nestedError);
            }
            else
            {
                WriteObject(item);
            }
        }, BodyScript,
            SqlInstance, SqlCredential, InputObject, EnableException.ToBool(),
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug"));
    }

    private object? BoundCommonParameter(string name)
    {
        if (MyInvocation.BoundParameters.TryGetValue(name, out object? value))
        {
            return LanguagePrimitives.IsTrue(value);
        }
        return null;
    }

    private void RemoveHopErrorBookkeeping(ErrorRecord record)
    {
        try
        {
            if (SessionState.PSVariable.GetValue("Error") is not ArrayList errorList || errorList.Count == 0)
            {
                return;
            }
            if (errorList[0] is not ErrorRecord first)
            {
                return;
            }
            if (ReferenceEquals(first, record) || ReferenceEquals(first.Exception, record.Exception) ||
                string.Equals(first.Exception?.Message, record.Exception?.Message, StringComparison.Ordinal))
            {
                errorList.RemoveAt(0);
            }
        }
        catch
        {
            // Best-effort bookkeeping only.
        }
    }

    private const string BodyScript = """
param($SqlInstance, $SqlCredential, $InputObject, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, $SqlCredential,
        [Microsoft.SqlServer.Management.Smo.Agent.Job[]]$InputObject, $EnableException)

    foreach ($instance in $SqlInstance) {
        try {
            $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Get-DbaRunningJob
        }

        # Refresh JobServer information (including childs) in case $instance is an smo to get up to date information.
        $server.JobServer.Jobs.Refresh($true)
        Get-DbaAgentJob -SqlInstance $server -IncludeExecution | Where-Object CurrentRunStatus -ne 'Idle'
    }
    foreach ($job in $InputObject) {
        # Refresh job to get up to date information.
        $job.Refresh()
        $job | Where-Object CurrentRunStatus -ne 'Idle'
    }
} $SqlInstance $SqlCredential $InputObject $EnableException @__commonParameters 3>&1 2>&1
""";
}
