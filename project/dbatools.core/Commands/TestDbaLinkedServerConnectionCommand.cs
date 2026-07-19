#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Tests every linked server's connectivity. Port of
/// public/Test-DbaLinkedServerConnection.ps1 (W3-111). WHOLE-RECORD verbatim hop
/// (process-only source). CLASSIFICATION TABLE (SqlInstance is the VFP; promoted
/// question answered): NO param mutations; all locals per-iteration - no sentinel. The
/// LinkedLive branch (a piped SMO LinkedServer converts to a DbaInstanceParameter
/// carrying the live object) and the catch's
/// $_.Exception.InnerException.InnerException.Message DOUBLE-INNER unwrap ride
/// verbatim. NO ShouldProcess (plain CmdletBinding, no WhatIf/Confirm plumbing); no
/// Test-FunctionInterrupt; the single Stop-Function uses -Continue INSIDE the
/// per-record foreach (the W3-102 relay and W3-103 latch classes verified N/A).
/// Hop-frame Stop-Function/Write-Message carry -FunctionName (W1-090). No bind-time
/// casts. Output objects are the library's own
/// Dataplat.Dbatools.Validation.LinkedServerResult, constructed verbatim. Surface
/// pinned by migration/baselines/Test-DbaLinkedServerConnection.json (no sets,
/// implicit positions: SqlInstance DbaInstanceParameter[] Mandatory pos0 VFP,
/// SqlCredential pos1).
/// </summary>
[Cmdlet(VerbsDiagnostic.Test, "DbaLinkedServerConnection")]
public sealed class TestDbaLinkedServerConnectionCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Credential for SQL Server authentication.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, ProcessScript,
            SqlInstance, SqlCredential, EnableException.ToBool(),
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                RemoveHopErrorBookkeeping(nestedError);
                WriteError(nestedError);
                continue;
            }
            WriteObject(item);
        }
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

    // PS: the ENTIRE process body VERBATIM per record. Substitutions only: explicit
    // -FunctionName Test-DbaLinkedServerConnection on hop-frame
    // Stop-Function/Write-Message (W1-090).
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, $EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    . {
        foreach ($instance in $SqlInstance) {
            if ($instance.LinkedLive) {
                $linkedServerCollection = $instance.LinkedServer
            } else {
                try {
                    $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential
                } catch {
                    Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Test-DbaLinkedServerConnection
                }
                $linkedServerCollection = $server.LinkedServers
            }

            foreach ($ls in $linkedServerCollection) {
                Write-Message -Level Verbose -Message "Testing linked server $($ls.name) on server $($ls.parent.name)" -FunctionName Test-DbaLinkedServerConnection -ModuleName "dbatools"
                try {
                    $null = $ls.TestConnection()
                    $result = "Success"
                    $connectivity = $true
                } catch {
                    $result = $_.Exception.InnerException.InnerException.Message
                    $connectivity = $false
                }

                New-Object Dataplat.Dbatools.Validation.LinkedServerResult($ls.parent.ComputerName, $ls.parent.ServiceName, $ls.parent.DomainInstanceName, $ls.Name, $ls.DataSource, $connectivity, $result)
            }
        }
    }
} $SqlInstance $SqlCredential $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
