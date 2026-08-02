#nullable enable

namespace Dataplat.Dbatools.Commands;

// Hop begin-script constant - split out per the repo 400-line file limit.
public sealed partial class GetDbaDbBackupHistoryCommand
{

    // Begin diagnostics VERBATIM (the two -Level System messages), with the carrier substitution for
    // $PSCmdlet.ParameterSetName / $PSBoundParameters.Keys which do not exist inside the hop.
    private const string BeginScript = """
param($__paramSetName, $__boundKeys, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param($__paramSetName, $__boundKeys, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }
        Write-Message -Level System -Message "Active Parameter set: $__paramSetName."
        Write-Message -Level System -Message "Bound parameters: $__boundKeys"
} $__paramSetName $__boundKeys $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
