#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Returns the compatibility level of each database. Port of public/Get-DbaDbCompatibility.ps1; the
/// workflow remains a module-scoped PowerShell compatibility hop.
///
/// A process-only port (InputObject is ValueFromPipeline, so process fires per record; the -SqlInstance
/// path gathers databases via Get-DbaDatabase). No begin/end, no accumulator, no interrupt. Two subtle
/// points. (1) The source guard `if (Test-Bound -not 'SqlInstance', 'InputObject')` (true iff NEITHER is
/// bound) becomes `if ((-not $__boundSqlInstance) -and (-not $__boundInputObject))` from two carried flags
/// (C# TestBound(nameof(SqlInstance)) / TestBound(nameof(InputObject))). (2) That guard runs Write-Message
/// then `continue` (source line 88) with NO enclosing loop - the foreach $db is later. In a process block
/// that continue skips the rest for the current pipeline item, but a bare continue inside the hop's
/// & $module { } scriptblock has no loop to continue and would PROPAGATE OUT (verified by probe). So the
/// entire body is wrapped in `foreach ($__continueGuard in @(1)) { ... }` - the continue exits the wrapper
/// (skipping the rest of the body for this record) without escaping the hop; the source's own foreach $db is
/// nested inside and has no continue of its own. Body edits: -FunctionName Get-DbaDbCompatibility on the two
/// Write-Message. Surface pinned by migration/baselines/Get-DbaDbCompatibility.json (positions 0-3, no sets,
/// no ShouldProcess).
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaDbCompatibility")]
public sealed class GetDbaDbCompatibilityCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The database(s) to check.</summary>
    [Parameter(Position = 2)]
    [PsStringArrayCast]
    public string[]? Database { get; set; }

    /// <summary>Database object(s) piped in.</summary>
    [Parameter(ValueFromPipeline = true, Position = 3)]
    public Microsoft.SqlServer.Management.Smo.Database[]? InputObject { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, ProcessScript,
            SqlInstance, SqlCredential, Database, InputObject, EnableException.ToBool(),
            TestBound(nameof(SqlInstance)), TestBound(nameof(InputObject)),
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
    // PS: the process block, wrapped in foreach ($__continueGuard in @(1)) so the guard's bare continue
    // (which has no enclosing loop) exits the wrapper instead of propagating out of the hop scriptblock.
    // Edits: Test-Bound -not 'SqlInstance','InputObject' -> the carried $__boundSqlInstance/$__boundInputObject
    // flags, and -FunctionName Get-DbaDbCompatibility on the two Write-Message.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Database, $InputObject, $EnableException, $__boundSqlInstance, $__boundInputObject, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [string[]]$Database, [Microsoft.SqlServer.Management.Smo.Database[]]$InputObject, $EnableException, $__boundSqlInstance, $__boundInputObject, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

    foreach ($__continueGuard in @(1)) {
        if ((-not $__boundSqlInstance) -and (-not $__boundInputObject)) {
            Write-Message -Level Warning -Message "You must specify either a SQL instance or pipe a database collection" -FunctionName Get-DbaDbCompatibility -ModuleName "dbatools"
            continue
        }

        if ($SqlInstance) {
            $InputObject += Get-DbaDatabase -SqlInstance $SqlInstance -SqlCredential $SqlCredential -Database $Database
        }

        foreach ($db in $InputObject) {
            $server = $db.Parent
            $ServerVersion = $server.VersionMajor
            Write-Message -Level Verbose -Message "SQL Server is using Version: $ServerVersion" -FunctionName Get-DbaDbCompatibility -ModuleName "dbatools"

            [PSCustomObject]@{
                ComputerName  = $server.ComputerName
                InstanceName  = $server.ServiceName
                SqlInstance   = $server.DomainInstanceName
                Database      = $db.Name
                DatabaseId    = $db.Id
                Compatibility = $db.CompatibilityLevel
            }
        }
    }
} $SqlInstance $SqlCredential $Database $InputObject $EnableException $__boundSqlInstance $__boundInputObject $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
