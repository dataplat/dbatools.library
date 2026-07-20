#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves partition scheme definitions and metadata from databases. Port of
/// public/Get-DbaDbPartitionScheme.ps1; the workflow remains a module-scoped PowerShell compatibility hop.
///
/// A process+end port (SqlInstance is ValueFromPipeline, so process fires per record). The process block is the
/// exact structural twin of Get-DbaDbPartitionFunction (W2-098): no accumulator, no interrupt (the one
/// Stop-Function is -Continue), no Test-Bound, no ShouldProcess; the two continue statements sit inside
/// foreach ($db) (loop-bound, so no continue-guard wrapper). The end block runs a single Test-DbaDeprecation
/// alias check (no carried state, no -FunctionName target). The only process edits are -FunctionName
/// Get-DbaDbPartitionScheme on the one Stop-Function and two Write-Message. Surface pinned by
/// migration/baselines/Get-DbaDbPartitionScheme.json (positions 0-4, PartitionScheme Name alias, no ShouldProcess).
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaDbPartitionScheme")]
public sealed class GetDbaDbPartitionSchemeCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The database(s) to search.</summary>
    [Parameter(Position = 2)]
    public object[]? Database { get; set; }

    /// <summary>The database(s) to exclude.</summary>
    [Parameter(Position = 3)]
    public object[]? ExcludeDatabase { get; set; }

    /// <summary>Filter to the specified partition scheme(s) by name.</summary>
    [Parameter(Position = 4)]
    [Alias("Name")]
    [PsStringArrayCast]
    public string[]? PartitionScheme { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

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
            SqlInstance, SqlCredential, Database, ExcludeDatabase, PartitionScheme, EnableException.ToBool(),
            BoundCommonParameter("Verbose"), BoundCommonParameter("Debug"));
    }

    protected override void EndProcessing()
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
        }, EndScript,
            EnableException.ToBool(), BoundCommonParameter("Verbose"), BoundCommonParameter("Debug"));
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
    // PS: the process block VERBATIM. Edit: -FunctionName Get-DbaDbPartitionScheme on the one Stop-Function
    // and two Write-Message. The two continue statements are inside foreach ($db) - loop-bound.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Database, $ExcludeDatabase, $PartitionScheme, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [object[]]$Database, [object[]]$ExcludeDatabase, [string[]]$PartitionScheme, $EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

        foreach ($instance in $SqlInstance) {
            try {
                $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential
            } catch {
                Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -FunctionName Get-DbaDbPartitionScheme -Continue
            }

            $databases = $server.Databases | Where-Object IsAccessible

            if ($Database) {
                $databases = $databases | Where-Object Name -In $Database
            }
            if ($ExcludeDatabase) {
                $databases = $databases | Where-Object Name -NotIn $ExcludeDatabase
            }

            foreach ($db in $databases) {
                if (!$db.IsAccessible) {
                    Write-Message -Level Warning -Message "Database $db is not accessible. Skipping." -FunctionName Get-DbaDbPartitionScheme -ModuleName "dbatools"
                    continue
                }

                $partitionSchemes = $db.PartitionSchemes

                if ($PartitionScheme) {
                    $partitionSchemes = $partitionSchemes | Where-Object { $_.Name -in $PartitionScheme }
                }

                if (!$partitionSchemes) {
                    Write-Message -Message "No Partition Schemes exist in the $db database on $instance" -Target $db -Level Verbose -FunctionName Get-DbaDbPartitionScheme -ModuleName "dbatools"
                    continue
                }

                $partitionSchemes | ForEach-Object {

                    Add-Member -Force -InputObject $_ -MemberType NoteProperty -Name ComputerName -value $server.ComputerName
                    Add-Member -Force -InputObject $_ -MemberType NoteProperty -Name InstanceName -value $server.ServiceName
                    Add-Member -Force -InputObject $_ -MemberType NoteProperty -Name SqlInstance -value $server.DomainInstanceName
                    Add-Member -Force -InputObject $_ -MemberType NoteProperty -Name Database -value $db.Name

                    Select-DefaultView -InputObject $_ -Property ComputerName, InstanceName, SqlInstance, Database, Name, PartitionFunction
                }
            }
        }
} $SqlInstance $SqlCredential $Database $ExcludeDatabase $PartitionScheme $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";

    // PS: the end block VERBATIM - a single Test-DbaDeprecation alias check, no carried state.
    private const string EndScript = """
param($EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param($EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

        Test-DbaDeprecation -DeprecatedOn "1.0.0" -EnableException:$false -Alias Get-DbaDatabasePartitionScheme
} $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
