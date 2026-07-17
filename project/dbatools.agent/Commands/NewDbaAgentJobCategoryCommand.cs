#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Creates a SQL Server Agent job category on the target instances.
/// </summary>
/// <remarks>
/// The SMO job-category construction, the CategoryType assignment, and the output run the original
/// dbatools PowerShell body inside the dbatools module scope rather than being reimplemented in C#, so
/// the engine decides the observable details.
///
/// The function's begin block resolves the CategoryType default ("LocalJob") once and emits a verbose
/// message announcing it. That default cannot be folded into the process hop: the process hop runs once
/// per pipeline record (and not at all for empty pipeline input), so folding would repeat the verbose
/// message per record and drop it entirely on @() input. So the begin runs as its own module-scope hop
/// that resolves CategoryType and carries it to the process hop. The begin also lowers the confirm
/// preference under -Force, but that only matters where the ShouldProcess gate runs (the process hop),
/// which re-establishes it in its own scope.
///
/// Output streams as it is produced. A single record can create categories across several instances,
/// and each is emitted before a later one may fail under -EnableException; buffering them and losing
/// them to a later terminating failure would hide categories that were actually created.
///
/// This cmdlet supplies the real ShouldProcess runtime to the process hop, and the gate selects
/// $PSCmdlet (whose confirm preference is lowered) under -Force and the real cmdlet otherwise, so a
/// bound -Confirm cannot be overridden on the real cmdlet's own runtime. Surface pinned by
/// migration/baselines/New-DbaAgentJobCategory.json.
/// </remarks>
[Cmdlet(VerbsCommon.New, "DbaAgentJobCategory", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Low)]
public sealed class NewDbaAgentJobCategoryCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    [PsDbaInstanceArrayCast]
    public DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>One or more job category names to create.</summary>
    [Parameter(Mandatory = true, Position = 2)]
    [ValidateNotNullOrEmpty]
    public string[] Category { get; set; } = null!;

    /// <summary>The scope of the job category: LocalJob, MultiServerJob, or None. Defaults to LocalJob.</summary>
    [Parameter(Position = 3)]
    [ValidateSet("LocalJob", "MultiServerJob", "None")]
    public string? CategoryType { get; set; }

    /// <summary>Suppress the confirmation prompt.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    // The CategoryType resolved once in the begin hop (the bound value, or "LocalJob"), carried to the
    // process hop where the SMO category is assigned it.
    private string? _categoryType;

    protected override void BeginProcessing()
    {
        if (Interrupted)
        {
            return;
        }

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, BeginScript,
            CategoryType, Force.ToBool(),
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
        {
            if (item?.BaseObject is Hashtable sentinel && sentinel.ContainsKey("__newDbaAgentJobCategoryBegin"))
            {
                if (sentinel["__newDbaAgentJobCategoryBegin"] is Hashtable state)
                {
                    _categoryType = state["CategoryType"] as string;
                }
                continue;
            }
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                RemoveHopErrorBookkeeping(nestedError);
                WriteError(nestedError);
                continue;
            }
            WriteObject(item);
        }
    }

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
        }, ProcessScript,
            SqlInstance, SqlCredential, Category, _categoryType, Force.ToBool(), EnableException.ToBool(), this,
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

    // PS: the begin block VERBATIM. It resolves the CategoryType default (emitting the verbose message
    // once) and reports the resolved value in a sentinel hashtable carried to the process hop. Edit:
    // -FunctionName New-DbaAgentJobCategory on the direct Write-Message.
    private const string BeginScript = """
param($CategoryType, $Force, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([string]$CategoryType, $Force)

    if ($Force) { $ConfirmPreference = 'none' }

    if (-not $CategoryType) {
        Write-Message -Message "Setting the category type to 'LocalJob'" -Level Verbose -FunctionName New-DbaAgentJobCategory
        $CategoryType = "LocalJob"
    }

    @{ __newDbaAgentJobCategoryBegin = @{ CategoryType = $CategoryType } }
} $CategoryType $Force @__commonParameters 3>&1 2>&1
""";

    // PS: the process block VERBATIM. Edits: $PSCmdlet.ShouldProcess -> $__gate.ShouldProcess (the gate
    // selector re-establishes the begin's -Force confirm lowering in this separate scope), and
    // -FunctionName New-DbaAgentJobCategory on the direct Stop-Function sites. CategoryType is the
    // carried begin result. There is no end block, so the newParent "+ return" simply exits this
    // record's hop, exactly as it exits the source process block for that record.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Category, $CategoryType, $Force, $EnableException, $__realCmdlet, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundWhatIf) { $__commonParameters.WhatIf = [bool]$__boundWhatIf }
if ($null -ne $__boundConfirm) { $__commonParameters.Confirm = [bool]$__boundConfirm }
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = "Low")]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, $SqlCredential, [string[]]$Category, [string]$CategoryType, $Force, $EnableException, $__realCmdlet)

    if ($Force) { $ConfirmPreference = 'none' }
    $__gate = if ($Force) { $PSCmdlet } else { $__realCmdlet }
    foreach ($instance in $SqlInstance) {
        try {
            $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName New-DbaAgentJobCategory
        }

        foreach ($cat in $Category) {
            if ($cat -in $server.JobServer.JobCategories.Name) {
                Stop-Function -Message "Job category $cat already exists on $instance" -Target $instance -Continue -FunctionName New-DbaAgentJobCategory
            } else {
                if ($__gate.ShouldProcess($instance, "Adding the job category $cat")) {
                    try {
                        try {
                            $jobCategory = New-Object Microsoft.SqlServer.Management.Smo.Agent.JobCategory($server.JobServer, $cat)
                        } catch {
                            if ($_.Exception.Message -match "newParent") {
                                Stop-Function -Message "Cannot create agent job category through a contained availability group listener. SQL Server Agent objects are instance-level and must be managed on the instance directly. Please connect to the primary replica instead of the listener. Use Get-DbaAvailabilityGroup to find the current primary replica." -ErrorRecord $_ -Target $cat -Continue -FunctionName New-DbaAgentJobCategory
                                return
                            } else {
                                throw
                            }
                        }
                        $jobCategory.CategoryType = $CategoryType

                        $jobCategory.Create()

                        $server.JobServer.Refresh()
                    } catch {
                        Stop-Function -Message "Something went wrong creating the job category $cat on $instance" -Target $cat -Continue -ErrorRecord $_ -FunctionName New-DbaAgentJobCategory
                    }
                }
            }
            Get-DbaAgentJobCategory -SqlInstance $server -Category $cat
        }
    }
} $SqlInstance $SqlCredential $Category $CategoryType $Force $EnableException $__realCmdlet @__commonParameters 3>&1 2>&1
""";
}
