#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;
using SmoJobCategory = Microsoft.SqlServer.Management.Smo.Agent.JobCategory;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Removes SQL Server Agent job categories.
/// </summary>
/// <remarks>
/// The category lookup (Get-DbaAgentJobCategory), the confirmation gate, the Drop, and the result-object
/// shaping all run the original dbatools PowerShell body inside the dbatools module scope rather than
/// being reimplemented in C#, so the engine decides the observable details.
///
/// The function collects every category across the whole pipeline in its begin/process blocks and only
/// drops them in end, to avoid "Collection was modified" when piped directly from Get-DbaAgentJobCategory.
/// The accumulator ($jobCategories) is pipeline-spanning state a per-record hop scope cannot hold, so it
/// lives in C#: begin seeds an empty list, each process record contributes its categories (a
/// Get-DbaAgentJobCategory lookup in the SqlInstance parameter set, or the bound InputObject in the
/// pipeline set), and the end hop receives the full list to drop.
///
/// The SqlInstance path reproduces the source's "$params = $PSBoundParameters; remove WhatIf/Confirm;
/// Get-DbaAgentJobCategory @params" by receiving this cmdlet's own MyInvocation.BoundParameters and
/// splatting it, so the lookup sees exactly the bound Category/CategoryType/SqlCredential/EnableException
/// supplied.
///
/// The end hop streams: it emits a result object per category as each Drop runs, before a later Drop may
/// throw under -EnableException, so buffering would hide categories that were actually dropped. The
/// process hop is buffered - Get-DbaAgentJobCategory is read-only, the mutation is entirely in end.
///
/// This cmdlet supplies the real ShouldProcess runtime to the end hop (ConfirmImpact High, no -Force).
/// Surface pinned by migration/baselines/Remove-DbaAgentJobCategory.json.
/// </remarks>
[Cmdlet(VerbsCommon.Remove, "DbaAgentJobCategory", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.High)]
public sealed class RemoveDbaAgentJobCategoryCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(ParameterSetName = "NonPipeline", Mandatory = true, Position = 0)]
    [PsDbaInstanceArrayCast]
    public DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(ParameterSetName = "NonPipeline")]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>Only remove these named job categories.</summary>
    [Parameter(ParameterSetName = "NonPipeline")]
    public string[]? Category { get; set; }

    /// <summary>Only remove job categories of these types.</summary>
    [Parameter(ParameterSetName = "NonPipeline")]
    [ValidateSet("LocalJob", "MultiServerJob", "None")]
    public string[]? CategoryType { get; set; }

    /// <summary>Agent job category objects piped in from Get-DbaAgentJobCategory.</summary>
    [Parameter(ParameterSetName = "Pipeline", Mandatory = true, ValueFromPipeline = true)]
    public SmoJobCategory[]? InputObject { get; set; }

    /// <summary>By default, when something goes wrong we try to catch it, interpret it and give you a friendly warning message. Using this switch turns this "nice by default" feature off and enables you to catch exceptions with your own try/catch.</summary>
    // EnableException is inherited from DbaBaseCmdlet (virtual); the source declares it in BOTH named
    // sets explicitly, so it is overridden here with the per-set [Parameter] attributes to match that
    // surface exactly (the inherited bare declaration would reflect as __AllParameterSets and diverge).
    [Parameter(ParameterSetName = "NonPipeline")]
    [Parameter(ParameterSetName = "Pipeline")]
    public override SwitchParameter EnableException { get; set; }

    // The pipeline-spanning accumulator: the source's begin "$jobCategories = @()", filled across process
    // records, drained in end.
    private List<PSObject> _jobCategories = null!;

    protected override void BeginProcessing()
    {
        _jobCategories = new List<PSObject>();
    }

    protected override void ProcessRecord()
    {
        if (Interrupted)
        {
            return;
        }

        // Reproduce "$params = $PSBoundParameters" faithfully: this cmdlet's own bound parameters (which
        // include CategoryType when the caller supplied it).
        Hashtable bound = new Hashtable(MyInvocation.BoundParameters);

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, ProcessScript,
            SqlInstance, InputObject, bound,
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                RemoveHopErrorBookkeeping(nestedError);
                WriteError(nestedError);
                continue;
            }
            if (item is not null)
            {
                _jobCategories.Add(item);
            }
        }
    }

    protected override void EndProcessing()
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
        }, EndScript,
            _jobCategories.ToArray(), EnableException.ToBool(), this,
            BoundCommonParameter("WhatIf"), BoundCommonParameter("Confirm"),
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

    // PS: the process block. The source assigns/appends to $jobCategories; here it EMITS the categories
    // and the C# accumulates them, because the accumulator must span the pipeline (which a per-record hop
    // scope cannot). The SqlInstance branch splats the caller's bound parameters (minus WhatIf/Confirm) to
    // Get-DbaAgentJobCategory exactly as the source's $PSBoundParameters re-splat did.
    private const string ProcessScript = """
param($SqlInstance, $InputObject, $__bound, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [Microsoft.SqlServer.Management.Smo.Agent.JobCategory[]]$InputObject, [hashtable]$__bound)

    if ($SqlInstance) {
        $params = $__bound
        $null = $params.Remove('WhatIf')
        $null = $params.Remove('Confirm')
        Get-DbaAgentJobCategory @params
    } else {
        $InputObject
    }
} $SqlInstance $InputObject $__bound @__commonParameters 3>&1 2>&1
""";

    // PS: the end block VERBATIM apart from $PSCmdlet.ShouldProcess -> $__realCmdlet.ShouldProcess and
    // -FunctionName Remove-DbaAgentJobCategory on the direct Stop-Function. $jobCategories is the
    // accumulated list the C# collected. EnableException is bound so Stop-Function's scope-walking default
    // inherits the caller's value (a Drop failure under -EnableException must throw).
    private const string EndScript = """
param($jobCategories, $EnableException, $__realCmdlet, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundWhatIf) { $__commonParameters.WhatIf = [bool]$__boundWhatIf }
if ($null -ne $__boundConfirm) { $__commonParameters.Confirm = [bool]$__boundConfirm }
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = "High")]
    param($jobCategories, $EnableException, $__realCmdlet)

    # We have to delete in the end block to prevent "Collection was modified; enumeration operation may not execute." if directly piped from Get-DbaAgentJobCategory.
    foreach ($jobCategory in $jobCategories) {
        if ($__realCmdlet.ShouldProcess($jobCategory.Parent.Parent.Name, "Removing the SQL Agent category(-ies) $($jobCategory.Name) on $($jobCategory.Parent.Parent.Name)")) {
            $output = [PSCustomObject]@{
                ComputerName = $jobCategory.Parent.Parent.ComputerName
                InstanceName = $jobCategory.Parent.Parent.ServiceName
                SqlInstance  = $jobCategory.Parent.Parent.DomainInstanceName
                Name         = $jobCategory.Name
                Status       = $null
                IsRemoved    = $false
            }
            try {
                $jobCategory.Drop()
                $output.Status = "Dropped"
                $output.IsRemoved = $true
            } catch {
                Stop-Function -Message "Failed removing the SQL Agent job category(-ies) $($jobCategory.Name) on $($jobCategory.Parent.Parent.Name)" -ErrorRecord $_ -FunctionName Remove-DbaAgentJobCategory
                $output.Status = (Get-ErrorMessage -Record $_)
                $output.IsRemoved = $false
            }
            $output
        }
    }
} $jobCategories $EnableException $__realCmdlet @__commonParameters 3>&1 2>&1
""";
}
