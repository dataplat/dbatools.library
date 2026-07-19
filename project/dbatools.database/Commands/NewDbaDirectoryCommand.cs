#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Creates a directory on a SQL Server host via xp_create_subdir. Port of
/// public/New-DbaDirectory.ps1; the workflow remains a module-scoped PowerShell compatibility hop.
///
/// NO EXPLICIT begin/process/end BLOCKS, so the ENTIRE FUNCTION BODY IS AN IMPLICIT end BLOCK - and
/// that is why this hop is invoked from EndProcessing rather than ProcessRecord, unlike every other
/// single-block row in this satellite, which all had an explicit "process {}". Mapping it to
/// ProcessRecord would run the body at a different point in the pipeline lifecycle than the source
/// does. No parameter is ValueFromPipeline, so there is no record axis either way, but the faithful
/// mapping is end.
///
/// A SOURCE BUG RIDES VERBATIM: line :89 does "$Path = $Path.Replace("'", "''")" INSIDE the
/// "foreach ($instance in $SqlInstance)" loop, so the escaping is CUMULATIVE - a second instance sees
/// an already-escaped path and doubles its quotes again. Because the whole body is one block inside a
/// single hop invocation, the hop reproduces this exactly and needs no carry; recorded here so no
/// reviewer reads it as port drift.
///
/// NO CARRY OF ANY KIND. Nothing is ValueFromPipeline, so process/end fires once and the cross-record
/// axis does not exist. $created (:102/:104) is assigned on BOTH branches of its try/catch before the
/// read at :111, and $server, $exists and $sql are each assigned before use within their own loop
/// iteration. Both detectors return zero candidates.
///
/// NO INTERRUPT BRIDGE: no Test-FunctionInterrupt in the source. Worth noting the shape at :105 -
/// that Stop-Function has NEITHER -Continue NOR a following return, so it sets the latch and
/// execution FALLS THROUGH to emit the object at :108 with Created = $false. That is the source's
/// behaviour and it rides verbatim; nothing reads the latch back.
///
/// NO Test-Bound, no .IsPresent, no $PSBoundParameters iteration, no $PSCmdlet.ParameterSetName, no
/// preference-variable assignment - the full pre-claim scan set is clean.
///
/// The one $Pscmdlet.ShouldProcess gate at :99 routes to the real cmdlet via $__realCmdlet, which
/// matters here because SupportsShouldProcess is declared with no ConfirmImpact (so it defaults to
/// Medium) and the gate wraps the actual directory creation. The two -Continue Stop-Function calls
/// (:86 connection failure, :94 path exists) skip the instance. EnableException crosses as a
/// SwitchParameter OBJECT received untyped, per B's combined rule. In-hop Stop-Function/Write-Message
/// calls carry -FunctionName. Positions 0-2 confirmed against the exported baseline; both
/// -SqlInstance and -Path are Mandatory. Surface pinned by migration/baselines/New-DbaDirectory.json.
/// </summary>
[Cmdlet(VerbsCommon.New, "DbaDirectory", SupportsShouldProcess = true)]
public sealed class NewDbaDirectoryCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    public DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>The directory path to create on the host.</summary>
    [Parameter(Mandatory = true, Position = 1)]
    [PsStringCast]
    public string Path { get; set; } = null!;

    /// <summary>Alternative credential for the target instances.</summary>
    [Parameter(Position = 2)]
    public PSCredential? SqlCredential { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void EndProcessing()
    {
        if (Interrupted)
            return;

        // Streaming, not buffered (DEF-001): directories are created instance by instance and each
        // result is emitted, so a buffered hop would discard the record of directories already
        // created when a later instance's failure terminated the hop under -EnableException.
        NestedCommand.InvokeScopedStreaming(this, item =>
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                RemoveHopErrorBookkeeping(nestedError);
                WriteError(nestedError);
                return;
            }
            WriteObject(item);
        }, EndScript,
            SqlInstance, Path, SqlCredential, EnableException, this,
            BoundCommonParameter("WhatIf"), BoundCommonParameter("Confirm"),
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug"));
    }

    private object? BoundCommonParameter(string name)
    {
        if (MyInvocation.BoundParameters.TryGetValue(name, out object? value))
            return LanguagePrimitives.IsTrue(value);
        return null;
    }

    private void RemoveHopErrorBookkeeping(ErrorRecord record)
    {
        try
        {
            if (SessionState.PSVariable.GetValue("Error") is not ArrayList errorList || errorList.Count == 0)
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
            // Best-effort bookkeeping only.
        }
    }

    // PS: the function body VERBATIM. There are no explicit begin/process/end blocks in the source,
    // so this IS the implicit end block - hence EndScript, invoked from EndProcessing. Edits: the one
    // $Pscmdlet gate routes to $__realCmdlet, plus -FunctionName stamps. The cumulative $Path
    // escaping at :89 rides untouched.
    private const string EndScript = """
param($SqlInstance, $Path, $SqlCredential, $EnableException, $__realCmdlet, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundWhatIf) { $__commonParameters.WhatIf = [bool]$__boundWhatIf }
if ($null -ne $__boundConfirm) { $__commonParameters.Confirm = [bool]$__boundConfirm }
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding(SupportsShouldProcess)]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [string]$Path, [PSCredential]$SqlCredential, $EnableException, $__realCmdlet, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    . {
    foreach ($instance in $SqlInstance) {
        try {
            $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName New-DbaDirectory
        }

        $Path = $Path.Replace("'", "''")

        $exists = Test-DbaPath -SqlInstance $server -Path $Path

        if ($exists) {
            Stop-Function -Message "$Path already exists" -Target $server -Continue -FunctionName New-DbaDirectory
        }

        $sql = "EXEC master.dbo.xp_create_subdir '$Path'"
        Write-Message -Level Debug -Message $sql -FunctionName New-DbaDirectory
        if ($__realCmdlet.ShouldProcess($path, "Creating a new path on $($server.name)")) {
            try {
                $null = $server.Query($sql)
                $created = $true
            } catch {
                $created = $false
                Stop-Function -Message "Failure" -ErrorRecord $_ -FunctionName New-DbaDirectory
            }

            [PSCustomObject]@{
                Server  = $instance
                Path    = $Path
                Created = $created
            }
        }
    }
    }
} $SqlInstance $Path $SqlCredential $EnableException $__realCmdlet $__boundWhatIf $__boundConfirm $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}