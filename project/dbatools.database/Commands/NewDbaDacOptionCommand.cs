#nullable enable

using System;
using System.Collections;
using System.Management.Automation;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Creates a DacFx options object for an export, extract, publish or import action. Port of
/// public/New-DbaDacOption.ps1; the workflow remains a module-scoped PowerShell compatibility hop.
///
/// PROCESS-ONLY, and NO parameter is ValueFromPipeline, so process fires exactly once. The
/// cross-record carry axis therefore does not exist for this row - stated explicitly rather than
/// skipped, because the local enumeration is what a parameter-only check misses elsewhere.
///
/// The whole body sits inside one $PScmdlet.ShouldProcess gate, which routes to the real cmdlet via
/// $__realCmdlet. Inside that gate the source defines a nested helper, New-DacObject, whose param
/// block carries a SCOPE-WALKING DEFAULT - "[hashtable]$Property = $Property". That resolves from
/// the enclosing scope, which inside the hop is the hop's own $Property parameter, so it keeps
/// working without special handling; it is called out here because the same idiom in a helper
/// defined OUTSIDE the hop would need the variable carried in.
///
/// Both Stop-Function calls omit -Continue, so each sets the module interrupt flag, but the source
/// has no Test-FunctionInterrupt and process runs once - nothing ever reads the flag, so none is
/// carried. The "Stop-Function; return" on the profile-load failure exits the dot-sourced body only.
/// In-hop Stop-Function calls carry -FunctionName. Implicit positions 0-3 are made explicit
/// (Type 0, Action 1, PublishXml 2, Property 3); the switch correctly carries none. Surface pinned
/// by migration/baselines/New-DbaDacOption.json.
/// </summary>
[Cmdlet(VerbsCommon.New, "DbaDacOption", SupportsShouldProcess = true)]
public sealed class NewDbaDacOptionCommand : DbaBaseCmdlet
{
    /// <summary>The package type the options apply to.</summary>
    [Parameter(Position = 0)]
    [ValidateSet("Dacpac", "Bacpac")]
    [PsStringCast]
    public string Type { get; set; } = "Dacpac";

    /// <summary>Whether the options describe a publish or an export.</summary>
    [Parameter(Mandatory = true, Position = 1)]
    [ValidateSet("Publish", "Export")]
    [PsStringCast]
    public string Action { get; set; } = null!;

    /// <summary>A publish profile XML whose DeployOptions are loaded into the result.</summary>
    [Parameter(Position = 2)]
    [PsStringCast]
    public string? PublishXml { get; set; }

    /// <summary>Property values applied to the generated options object.</summary>
    [Parameter(Position = 3)]
    public Hashtable? Property { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, ProcessScript,
            Type, Action, PublishXml, Property, EnableException.ToBool(), this,
            BoundCommonParameter("WhatIf"), BoundCommonParameter("Confirm"),
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

    // PS: the process block VERBATIM, dot-sourced so the profile-load "Stop-Function; return" exits
    // only the body. Edits: the one $PScmdlet gate routes to $__realCmdlet and -FunctionName is
    // stamped on the two Stop-Function calls. The nested New-DacObject helper and its scope-walking
    // "[hashtable]$Property = $Property" default ride verbatim - inside the hop that default
    // resolves to the hop's own $Property parameter.
    private const string ProcessScript = """
param($Type, $Action, $PublishXml, $Property, $EnableException, $__realCmdlet, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundWhatIf) { $__commonParameters.WhatIf = [bool]$__boundWhatIf }
if ($null -ne $__boundConfirm) { $__commonParameters.Confirm = [bool]$__boundConfirm }
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding(SupportsShouldProcess)]
    param([string]$Type, [string]$Action, [string]$PublishXml, [hashtable]$Property, $EnableException, $__realCmdlet, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    . {
        if ($__realCmdlet.ShouldProcess("$type", "Creating New DacOptions of $action")) {
            function New-DacObject {
                Param ([String]$TypeName, [hashtable]$Property = $Property)

                $dacOptionSplat = @{TypeName = $TypeName }
                if ($Property) { $dacOptionSplat.Property = $Property }
                try {
                    New-Object @dacOptionSplat -ErrorAction Stop
                } catch {
                    Stop-Function -Message "Could not generate object $TypeName" -ErrorRecord $_ -FunctionName New-DbaDacOption
                }
            }

            # Pick proper option object depending on type and action
            if ($Action -eq 'Export') {
                if ($Type -eq 'Dacpac') {
                    New-DacObject -TypeName Microsoft.SqlServer.Dac.DacExtractOptions
                } elseif ($Type -eq 'Bacpac') {
                    New-DacObject -TypeName Microsoft.SqlServer.Dac.DacExportOptions
                }
            } elseif ($Action -eq 'Publish') {
                if ($Type -eq 'Dacpac') {
                    $output = New-DacObject -TypeName Microsoft.SqlServer.Dac.PublishOptions
                    if ($PublishXml) {
                        try {
                            $dacProfile = [Microsoft.SqlServer.Dac.DacProfile]::Load($PublishXml)
                            $output.DeployOptions = $dacProfile.DeployOptions
                        } catch {
                            Stop-Function -Message "Could not load profile." -ErrorRecord $_ -FunctionName New-DbaDacOption
                            return
                        }
                    } else {
                        $output.DeployOptions = if ($Property -and 'DeployOptions' -in $Property.Keys) {
                            New-DacObject -TypeName Microsoft.SqlServer.Dac.DacDeployOptions -Property $Property.DeployOptions
                        } else {
                            New-DacObject -TypeName Microsoft.SqlServer.Dac.DacDeployOptions -Property @{ }
                        }
                    }
                    if ($null -eq $Property.GenerateDeploymentScript) {
                        $output.GenerateDeploymentScript = $false
                    }
                    $output
                } elseif ($Type -eq 'Bacpac') {
                    New-DacObject -TypeName Microsoft.SqlServer.Dac.DacImportOptions
                }
            }
        }
    }
} $Type $Action $PublishXml $Property $EnableException $__realCmdlet $__boundWhatIf $__boundConfirm $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}