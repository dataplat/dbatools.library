#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Removes user defined functions from one or more databases. Port of public/Remove-DbaDbUdf.ps1
/// (W2-172); the workflow remains a module-scoped PowerShell compatibility hop.
///
/// A THREE-BLOCK accumulate-then-drop row, built on the RemoveDbaCredentialCommand exemplar (W3-072)
/// in dbatools.core, which has the identical shape. Two mechanics carry it, and neither is optional:
///
/// 1. THE ACCUMULATOR. begin does `$udfs = @( )`, process either REASSIGNS it (the -SqlInstance
///    branch) or APPENDS to it (`$udfs += $InputObject`, once per piped record), and END consumes it.
///    Because each ProcessRecord is a separate InvokeScoped call, the accumulator would reset every
///    record and the end block would see nothing. It rides the __w2172State sentinel: $null on the
///    first record reproduces begin's `@( )` init (which had no other effect), and every record
///    returns the accumulator through the sentinel for the next one and finally for the end hop.
///    The source's own end-block comment explains WHY the drop is deferred - deleting while
///    enumerating a collection piped straight from Get-DbaDbUdf throws "Collection was modified" -
///    so the accumulate-in-end shape is load-bearing and must not be flattened into process.
///
/// 2. THE $PSBoundParameters SPLAT. The process body does `$params = $PSBoundParameters`, removes
///    WhatIf and Confirm, then splats @params straight into Get-DbaDbUdf. Inside a hop the
///    scriptblock has its OWN $PSBoundParameters - the carried hop arguments - so a verbatim carry
///    would splat $__state and $__boundVerbose into Get-DbaDbUdf and fail. It therefore rides as a
///    per-record `new Hashtable(MyInvocation.BoundParameters)` clone of THIS cmdlet's bound
///    parameters, and the verbatim Remove lines then run against that clone. Behaviourally identical
///    for identical invocations: the source mutates the live dictionary once, the clone re-supplies
///    the keys each record and removes them again before its only use.
///
/// ShouldProcess is real at HIGH impact (baseline: supportsShouldProcess true, confirmImpact High),
/// gated once in the end block, so $PSCmdlet.ShouldProcess becomes $__realCmdlet.ShouldProcess with
/// target and action strings byte-for-byte. The end hop also carries -WhatIf/-Confirm explicitly
/// through $__commonParameters, per the exemplar.
///
/// The catch path calls Stop-Function WITHOUT -Continue and STILL falls through to emit the failure
/// status object in non-EnableException mode. That is source-verbatim and deliberate - do not
/// "tidy" it into a continue. The private Get-ErrorMessage helper resolves because the hop runs in
/// module scope.
///
/// PARAMETER SETS mirrored from the baseline: NonPipeline (SqlInstance MANDATORY at position 0, plus
/// every filter parameter) and Pipeline (InputObject MANDATORY ValueFromPipeline). The source
/// declares EnableException in BOTH sets explicitly, so the virtual base property is OVERRIDDEN here
/// to carry both per-set attributes - the binder reads the most-derived declaration. There is NO
/// DefaultParameterSetName: the source's CmdletBinding declares none and the baseline records none,
/// so adding one would change which set an ambiguous invocation resolves to.
///
/// Pre-port DEF-012 detector returns clean for this body, and that is EXPECTED rather than
/// reassuring: the tool reads the process block only, while this accumulator is consumed in `end`.
/// That limitation is documented on the tool itself; the carry here was found by reading the block
/// structure, which is how this shape is always found.
///
/// Only other body edit is -FunctionName Remove-DbaDbUdf on the direct Stop-Function site.
///
/// Surface pinned by migration/baselines/Remove-DbaDbUdf.json
/// (sourceSha256 a6a0537fd5fcb015557f9db7bc1a8a8696fafdc33efdd03d41c2306d12a1bc4c).
/// </summary>
[Cmdlet(VerbsCommon.Remove, "DbaDbUdf", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.High)]
public sealed class RemoveDbaDbUdfCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(ParameterSetName = "NonPipeline", Mandatory = true, Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(ParameterSetName = "NonPipeline")]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The database(s) to process.</summary>
    [Parameter(ParameterSetName = "NonPipeline")]
    public string[]? Database { get; set; }

    /// <summary>The database(s) to exclude.</summary>
    [Parameter(ParameterSetName = "NonPipeline")]
    public object[]? ExcludeDatabase { get; set; }

    /// <summary>Exclude system user defined functions.</summary>
    [Parameter(ParameterSetName = "NonPipeline")]
    public SwitchParameter ExcludeSystemUdf { get; set; }

    /// <summary>The schema(s) to process.</summary>
    [Parameter(ParameterSetName = "NonPipeline")]
    public string[]? Schema { get; set; }

    /// <summary>The schema(s) to exclude.</summary>
    [Parameter(ParameterSetName = "NonPipeline")]
    public string[]? ExcludeSchema { get; set; }

    /// <summary>The function name(s) to process.</summary>
    [Parameter(ParameterSetName = "NonPipeline")]
    public string[]? Name { get; set; }

    /// <summary>The function name(s) to exclude.</summary>
    [Parameter(ParameterSetName = "NonPipeline")]
    public string[]? ExcludeName { get; set; }

    /// <summary>User defined function object(s) piped in.</summary>
    [Parameter(ParameterSetName = "Pipeline", Mandatory = true, ValueFromPipeline = true)]
    public object[]? InputObject { get; set; }

    /// <summary>Replaces friendly warnings with terminating exceptions. Declared in BOTH sets by the
    /// source, so the virtual base declaration is overridden to carry both per-set attributes.</summary>
    [Parameter(ParameterSetName = "NonPipeline")]
    [Parameter(ParameterSetName = "Pipeline")]
    public override SwitchParameter EnableException { get; set; }

    // The cross-block $udfs accumulator (begin inits, process appends per record, end drops).
    private Hashtable? _state;

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, ProcessScript,
            SqlInstance, InputObject, new Hashtable(MyInvocation.BoundParameters),
            EnableException.ToBool(), _state,
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug")))
        {
            if (item?.BaseObject is Hashtable sentinel && sentinel.ContainsKey("__w2172State"))
            {
                _state = sentinel["__w2172State"] as Hashtable;
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

    protected override void EndProcessing()
    {
        if (Interrupted)
            return;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, EndScript,
            EnableException.ToBool(), _state, this,
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

    // PS: the process body VERBATIM per record. Substitutions only: $PSBoundParameters -> the carried
    // $__boundParameters clone (the verbatim Remove lines then run against it), and the $udfs
    // accumulator restores from the sentinel ($null on the first record = the begin block's @( )
    // init, which had no other effect) and returns through it.
    private const string ProcessScript = """
param($SqlInstance, $InputObject, $__boundParameters, $EnableException, $__state, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [object[]]$InputObject, $__boundParameters, $EnableException, $__state, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    # begin-block init on the first record; later records restore the accumulator
    if ($null -eq $__state) {
        $udfs = @( )
    } else {
        $udfs = $__state.udfs
    }

    if ($SqlInstance) {
        $params = $__boundParameters
        $null = $params.Remove('WhatIf')
        $null = $params.Remove('Confirm')
        $udfs = Get-DbaDbUdf @params
    } else {
        $udfs += $InputObject
    }

    @{ __w2172State = @{ udfs = $udfs } }
} $SqlInstance $InputObject $__boundParameters $EnableException $__state $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";

    // PS: the end block VERBATIM. Substitutions only: $PSCmdlet -> $__realCmdlet and -FunctionName
    // Remove-DbaDbUdf on Stop-Function. The comment, and the fall-through that still emits the
    // failure status object after a Stop-Function with no -Continue, are the source's own.
    private const string EndScript = """
param($EnableException, $__state, $__realCmdlet, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundWhatIf) { $__commonParameters.WhatIf = [bool]$__boundWhatIf }
if ($null -ne $__boundConfirm) { $__commonParameters.Confirm = [bool]$__boundConfirm }
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = "High")]
    param($EnableException, $__state, $__realCmdlet, $__boundWhatIf, $__boundConfirm, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    $udfs = if ($null -eq $__state) { @( ) } else { $__state.udfs }

    # We have to delete in the end block to prevent "Collection was modified; enumeration operation may not execute." if directly piped from Get-DbaDbUdf.
    foreach ($udfItem in $udfs) {
        if ($__realCmdlet.ShouldProcess($udfItem.Parent.Parent.Name, "Removing the user defined function $($udfItem.Schema).$($udfItem.Name) in the database $($udfItem.Parent.Name) on $($udfItem.Parent.Parent.Name)")) {
            $output = [PSCustomObject]@{
                ComputerName = $udfItem.Parent.Parent.ComputerName
                InstanceName = $udfItem.Parent.Parent.ServiceName
                SqlInstance  = $udfItem.Parent.Parent.DomainInstanceName
                Database     = $udfItem.Parent.Name
                Udf          = "$($udfItem.Schema).$($udfItem.Name)"
                UdfName      = $udfItem.Name
                UdfSchema    = $udfItem.Schema
                Status       = $null
                IsRemoved    = $false
            }
            try {
                $udfItem.Drop()
                $output.Status = "Dropped"
                $output.IsRemoved = $true
            } catch {
                Stop-Function -Message "Failed removing the user defined function $($udfItem.Schema).$($udfItem.Name) in the database $($udfItem.Parent.Name) on $($udfItem.Parent.Parent.Name)" -ErrorRecord $_ -FunctionName Remove-DbaDbUdf
                $output.Status = (Get-ErrorMessage -Record $_)
                $output.IsRemoved = $false
            }
            $output
        }
    }
} $EnableException $__state $__realCmdlet $__boundWhatIf $__boundConfirm $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
