#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves Policy-Based Management category subscriptions from one or more SQL Server instances.
/// </summary>
/// <remarks>
/// <para>
/// The workflow remains a module-scoped PowerShell compatibility hop so that the PBM library load, the
/// store enumeration, the subscription retrieval, the default view, and dbatools stream and error handling
/// stay observable-identical to the script implementation.
/// </para>
/// <para>
/// The script has a begin block (Add-PbmLibrary) and a process block that emits. The port keeps that
/// structure faithfully as TWO hops: BeginProcessing runs the begin body once - matching the script's
/// begin-once semantics, including the empty-pipeline case where begin runs but process does not - and
/// ProcessRecord runs the process body once per pipeline record. Store objects piped through InputObject
/// bind per record; when SqlInstance is supplied instead, the body appends the resolved stores within
/// that record. The body has no Stop-Function, try/catch, or other terminating path, so there is no
/// earlier-output-before-later-throw exposure and buffered InvokeScoped is correct. EnableException is
/// carried as a plain (untyped) value, because a switch in the inner CmdletBinding scriptblock is excluded
/// from positional binding.
/// </para>
/// </remarks>
[Cmdlet(VerbsCommon.Get, "DbaPbmCategorySubscription")]
public sealed class GetDbaPbmCategorySubscriptionCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Alternative credential for the target instances.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>Policy store objects, typically piped from Get-DbaPbmStore.</summary>
    [Parameter(ValueFromPipeline = true, Position = 2)]
    public PSObject[]? InputObject { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    /// <summary>Loads the PBM library once, before any records are processed.</summary>
    protected override void BeginProcessing()
    {
        if (Interrupted)
            return;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, BeginScript,
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

    /// <summary>Retrieves category subscriptions for one pipeline record.</summary>
    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, ProcessScript,
            SqlInstance, SqlCredential, InputObject, EnableException.ToBool(),
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

    // PS: the begin body (Add-PbmLibrary) VERBATIM, no edits.
    private const string BeginScript = """
param($__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param($__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

        Add-PbmLibrary
} $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1

""";

    // PS: the process body VERBATIM. Substitution only: -FunctionName on the 1 direct Write-Message call.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $InputObject, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [System.Management.Automation.PSCredential]$SqlCredential, [psobject[]]$InputObject, $EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

        if (Test-FunctionInterrupt) { return }
        foreach ($instance in $SqlInstance) {
            $InputObject += Get-DbaPbmStore -SqlInstance $instance -SqlCredential $SqlCredential
        }
        foreach ($store in $InputObject) {
            $all = $store.PolicycategorySubscriptions

            foreach ($current in $all) {
                Write-Message -Level Verbose -Message "Processing $current" -FunctionName Get-DbaPbmCategorySubscription
                Add-Member -Force -InputObject $current -MemberType NoteProperty ComputerName -value $store.ComputerName
                Add-Member -Force -InputObject $current -MemberType NoteProperty InstanceName -value $store.InstanceName
                Add-Member -Force -InputObject $current -MemberType NoteProperty SqlInstance -value $store.SqlInstance
                Select-DefaultView -InputObject $current -ExcludeProperty Properties, Urn, Parent
            }
        }
} $SqlInstance $SqlCredential $InputObject $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1

""";
}
