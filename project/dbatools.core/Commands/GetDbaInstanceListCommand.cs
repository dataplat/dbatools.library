#nullable enable

using System;
using System.Collections;
using System.Management.Automation;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Returns the user-maintained tab-completion instance list. Port of
/// public/Get-DbaInstanceList.ps1 (W3-036). This command has an EMPTY param() - no parameters at all,
/// not even EnableException - so it deliberately inherits PSCmdlet DIRECTLY rather than DbaBaseCmdlet:
/// DbaBaseCmdlet contributes an EnableException parameter, which would ADD a parameter the retired
/// function never had and break the surface. NestedCommand.InvokeScopedStreaming takes a plain
/// PSCmdlet host, so the compatibility hop still works. The process body is a single
/// Get-DbatoolsConfigValue call (one terminal emit, no loop, no Stop-Function), so DEF-001 is not in
/// play; the hop still runs through InvokeScopedStreaming for uniformity. The body is fully verbatim.
/// Surface pinned by migration/baselines/Get-DbaInstanceList.json.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaInstanceList")]
public sealed class GetDbaInstanceListCommand : PSCmdlet
{
    protected override void ProcessRecord()
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
        }, ProcessScript,
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

    // PS: the process body VERBATIM (a single Get-DbatoolsConfigValue call).
    private const string ProcessScript = """
param($__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param($__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    Get-DbatoolsConfigValue -FullName "TabExpansion.KnownInstances" -Fallback @()
} $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
