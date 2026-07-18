#nullable enable

using System;
using System.Collections;
using System.Management.Automation;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Copies Extended Event session templates from the dbatools repository to the SSMS template directory
/// for GUI access.
/// </summary>
/// <remarks>
/// The destination-directory creation, the Get-DbaXESessionTemplate enumeration, the non-Microsoft
/// filter, and the Copy-Item all run the original dbatools PowerShell body VERBATIM inside the dbatools
/// module scope rather than being reimplemented in C#, so the engine decides the observable details.
///
/// Process-only, non-mutating on the pipeline (there is no pipeline-bound parameter, so ProcessRecord
/// runs exactly once). The two Stop-Function calls are no-Continue and gain -FunctionName
/// Copy-DbaXESessionTemplate; the Write-Message likewise. The source's opening
/// "if (Test-FunctionInterrupt) { return }" is kept verbatim but is inert here (a single invocation with
/// nothing setting the interrupt before that line), so no C# interrupt guard or carry is needed.
///
/// The two parameter defaults are applied INSIDE the hop when the parameter was not bound, not as C#
/// property initializers: the source default "$script:PSModuleRoot\bin\XEtemplates" for -Path resolves
/// the module-scope $script:PSModuleRoot (empty outside the module), and "$home\..." for -Destination
/// resolves the $home automatic variable; both evaluate faithfully only inside the module scope (the
/// W1-087 unbound-param law). Write-Message -Level Output can emit before a later Copy-Item throws under
/// -EnableException, so the hop uses InvokeScopedStreaming. Surface pinned by
/// migration/baselines/Copy-DbaXESessionTemplate.json.
/// </remarks>
[Cmdlet(VerbsCommon.Copy, "DbaXESessionTemplate")]
public sealed class CopyDbaXESessionTemplateCommand : DbaBaseCmdlet
{
    /// <summary>The directory or directories containing Extended Event session template files to copy from.</summary>
    [Parameter(Position = 0)]
    public string[]? Path { get; set; }

    /// <summary>The target directory where Extended Event templates will be installed for SSMS access.</summary>
    [Parameter(Position = 1)]
    public string? Destination { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - the source declares it bare, which the inherited
    // [Parameter] already matches; no override needed.

    protected override void ProcessRecord()
    {
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
            Path, Destination, EnableException.ToBool(), TestBound("Path"), TestBound("Destination"),
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

    // PS: the process block VERBATIM apart from -FunctionName Copy-DbaXESessionTemplate on the two
    // Stop-Function and the Write-Message sites, plus the two unbound-parameter defaults applied inside the
    // module scope (where $script:PSModuleRoot and $home resolve). EnableException is bound so
    // Stop-Function's scope-walking default inherits the caller's value.
    private const string ProcessScript = """
param($Path, $Destination, $EnableException, $__boundPath, $__boundDestination, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([string[]]$Path, [string]$Destination, $EnableException, $__boundPath, $__boundDestination)
    if (-not $__boundPath) { $Path = "$script:PSModuleRoot\bin\XEtemplates" }
    if (-not $__boundDestination) { $Destination = "$home\Documents\SQL Server Management Studio\Templates\XEventTemplates" }
    if (Test-FunctionInterrupt) { return }
    foreach ($destinstance in $Destination) {
        if (-not (Test-Path -Path $destinstance)) {
            try {
                $null = New-Item -ItemType Directory -Path $destinstance -ErrorAction Stop
            } catch {
                Stop-Function -Message "Failure" -ErrorRecord $_ -Target $destinstance -FunctionName Copy-DbaXESessionTemplate
            }
        }
        try {
            $files = (Get-DbaXESessionTemplate -Path $Path | Where-Object Source -ne Microsoft).Path
            foreach ($file in $files) {
                Write-Message -Level Output -Message "Copying $($file.Name) to $destinstance." -FunctionName Copy-DbaXESessionTemplate
                Copy-Item -Path $file -Destination $destinstance -ErrorAction Stop
            }
        } catch {
            Stop-Function -Message "Failure" -ErrorRecord $_ -Target $path -FunctionName Copy-DbaXESessionTemplate
        }
    }
} $Path $Destination $EnableException $__boundPath $__boundDestination @__commonParameters 3>&1 2>&1
""";
}
