#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Compares each database compatibility level against its server level. Port of
/// public/Test-DbaDbCompatibility.ps1; the workflow remains a module-scoped PowerShell
/// compatibility hop.
///
/// A process-only port. -SqlInstance is Mandatory and ValueFromPipeline, so process fires per piped
/// instance and the body's own foreach walks the array form; there is no -InputObject, no accumulator,
/// no begin/end and no interrupt handling of its own. The body needs no flag substitution at all: it
/// contains no Test-Bound (the -Database / -ExcludeDatabase filters test the VARIABLES), and no bare
/// continue, so no continue-guard wrapper is required either. The only body edit is the attribution
/// stamp -FunctionName Test-DbaDbCompatibility on the two sites called directly from the body
/// (Stop-Function on the connection failure, and the per-database Write-Message).
///
/// Surface pinned by migration/baselines/Test-DbaDbCompatibility.json: SqlInstance Mandatory
/// ValueFromPipeline at position 0, SqlCredential 1, Database 2, ExcludeDatabase 3 (both Object[],
/// not string[]), no parameter sets, no ShouldProcess. The source also declares
/// [OutputType("System.Collections.ArrayList")] even though it emits PSCustomObject records; that
/// declaration is cosmetic metadata on the function and is not reproduced as behavior here - the
/// emitted objects are what the tests and the baseline pin.
/// </summary>
[Cmdlet(VerbsDiagnostic.Test, "DbaDbCompatibility")]
public sealed class TestDbaDbCompatibilityCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The database(s) to process.</summary>
    [Parameter(Position = 2)]
    public object[]? Database { get; set; }

    /// <summary>The database(s) to exclude.</summary>
    [Parameter(Position = 3)]
    public object[]? ExcludeDatabase { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, ProcessScript,
            SqlInstance, SqlCredential, Database, ExcludeDatabase, EnableException.ToBool(),
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

    // PS: the process block verbatim. Only edit: -FunctionName Test-DbaDbCompatibility on the
    // Stop-Function and the per-database Write-Message, both called directly from the body.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Database, $ExcludeDatabase, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [object[]]$Database, [object[]]$ExcludeDatabase, $EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    foreach ($instance in $SqlInstance) {
        try {
            $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential -MinimumVersion 10
        } catch {
            Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Test-DbaDbCompatibility
        }

        $serverVersion = $server.VersionMajor
        $serverLevel = [Microsoft.SqlServer.Management.Smo.CompatibilityLevel]"Version$($serverVersion)0"
        $dbs = $server.Databases

        if ($Database) {
            $dbs = $dbs | Where-Object { $Database -contains $_.Name }
        }

        if ($ExcludeDatabase) {
            $dbs = $dbs | Where-Object Name -NotIn $ExcludeDatabase
        }

        foreach ($db in $dbs) {
            Write-Message -Level Verbose -Message "Processing $($db.name) on $instance." -FunctionName Test-DbaDbCompatibility
            [PSCustomObject]@{
                ComputerName          = $server.ComputerName
                InstanceName          = $server.ServiceName
                SqlInstance           = $server.DomainInstanceName
                ServerLevel           = $serverLevel
                Database              = $db.name
                DatabaseCompatibility = $db.CompatibilityLevel
                IsEqual               = $db.CompatibilityLevel -eq $serverLevel
            }
        }
    }
} $SqlInstance $SqlCredential $Database $ExcludeDatabase $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
