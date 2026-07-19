#nullable enable

using System;
using System.Collections;
using System.Management.Automation;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Gets cached CIM/WMI connections. Port of public/Get-DbaCmConnection.ps1 (W3-023). Three blocks:
/// the begin and end blocks are once-only Write-Message diagnostics (no state, no emit) and the
/// process block does the per-name filtering, so the port keeps them as three hops (BeginProcessing /
/// ProcessRecord / EndProcessing) rather than inlining - which would repeat the "Starting" / "Ending"
/// / bound-parameters diagnostics per pipeline record. The begin block's
/// "$PSBoundParameters.Keys -join ..." must reflect the REAL cmdlet's bound parameters, not the
/// scriptblock's (which would leak the hop's carrier names), so the joined key string is carried.
/// DEF-001 is not in play: the process emits a filtered in-memory collection with no throw. No
/// ShouldProcess. Positions match the retired function (ComputerName=0, UserName=1;
/// EnableException=switch/null) and the ComputerName alias (Filter) and constant defaults ("*") are
/// preserved. The body is otherwise verbatim. Surface pinned by
/// migration/baselines/Get-DbaCmConnection.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaCmConnection")]
public sealed class GetDbaCmConnectionCommand : DbaBaseCmdlet
{
    /// <summary>Computer name filter(s). Defaults to all cached connections.</summary>
    [Parameter(Position = 0, ValueFromPipeline = true)]
    [Alias("Filter")]
    public string[]? ComputerName { get; set; } = new[] { "*" };

    /// <summary>Username filter. Defaults to all.</summary>
    [Parameter(Position = 1)]
    public string UserName { get; set; } = "*";

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void BeginProcessing()
    {
        string boundKeys = string.Join(", ", MyInvocation.BoundParameters.Keys);
        NestedCommand.InvokeScopedStreaming(this, item =>
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                RemoveHopErrorBookkeeping(nestedError);
                WriteError(nestedError);
                return;
            }
            WriteObject(item);
        }, BeginScript,
            boundKeys, BoundCommonParameter("Verbose"), BoundCommonParameter("Debug"));
    }

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        NestedCommand.InvokeScopedStreaming(this, item =>
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                RemoveHopErrorBookkeeping(nestedError);
                WriteError(nestedError);
                return;
            }
            WriteObject(item);
        }, ProcessScript,
            ComputerName, UserName, EnableException.ToBool(),
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug"));
    }

    protected override void EndProcessing()
    {
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

    // PS: the begin block. $PSBoundParameters.Keys -join ", " -> the carried real bound-key string.
    private const string BeginScript = """
param($__boundKeys, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param($__boundKeys, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    Write-Message -Level InternalComment -Message "Starting"
    Write-Message -Level Verbose -Message "Bound parameters: $__boundKeys"
} $__boundKeys $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";

    // PS: the process body VERBATIM per record.
    private const string ProcessScript = """
param($ComputerName, $UserName, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([String[]]$ComputerName, [String]$UserName, $EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    foreach ($name in $ComputerName) {
        Write-Message -Level VeryVerbose -Message "Processing search. ComputerName: '$name' | Username: '$UserName'"
        ([Dataplat.Dbatools.Connection.ConnectionHost]::Connections.Values | Where-Object { ($_.ComputerName -like $name) -and ($_.Credentials.UserName -like $UserName) })
    }
} $ComputerName $UserName $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";

    // PS: the end block.
    private const string EndScript = """
param($__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param($__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    Write-Message -Level InternalComment -Message "Ending"
} $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
