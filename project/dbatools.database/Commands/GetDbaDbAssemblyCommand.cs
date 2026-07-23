#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves CLR assembly information from SQL Server databases (security level, owner, version, etc.).
/// Port of public/Get-DbaDbAssembly.ps1; the workflow remains a module-scoped PowerShell compatibility hop.
///
/// A process-only port (SqlInstance is ValueFromPipeline, so process fires per record); the simplest kind -
/// no begin/end, no accumulator, no interrupt (both Stop-Function calls are -Continue and there is no
/// Test-FunctionInterrupt or early return). The only sanctioned edits are the two Test-Bound reads -
/// Test-Bound 'Database' and Test-Bound 'Name' become carried boolean flags ($__boundDatabase / $__boundName,
/// from C# TestBound(...)) - and -FunctionName Get-DbaDbAssembly on the two direct Stop-Function calls.
/// Surface pinned by migration/baselines/Get-DbaDbAssembly.json (positions 0-3, no sets, no ShouldProcess).
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaDbAssembly")]
public sealed class GetDbaDbAssemblyCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The database(s) to search.</summary>
    [Parameter(Position = 2)]
    [PsStringArrayCast]
    public string[]? Database { get; set; }

    /// <summary>The assembly name(s) to filter by.</summary>
    [Parameter(Position = 3)]
    [PsStringArrayCast]
    public string[]? Name { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        NestedCommand.InvokeScopedStreaming(this, item =>
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                NestedCommand.RemoveDuplicateError(this, nestedError);
                WriteError(nestedError);
                return;
            }
            WriteObject(item);
        }, ProcessScript,
            SqlInstance, SqlCredential, Database, Name, EnableException.ToBool(),
            TestBound(nameof(Database)), TestBound(nameof(Name)),
            NestedCommand.BoundCommonParameter(this, "Verbose"), NestedCommand.BoundCommonParameter(this, "Debug"));
    }
    // PS: the process block VERBATIM. Edits: Test-Bound 'Database'/'Name' -> the carried $__boundDatabase/
    // $__boundName flags, and -FunctionName Get-DbaDbAssembly on the two direct Stop-Function calls.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Database, $Name, $EnableException, $__boundDatabase, $__boundName, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [string[]]$Database, [string[]]$Name, $EnableException, $__boundDatabase, $__boundName, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

        foreach ($instance in $SqlInstance) {
            try {
                $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential
            } catch {
                Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -Continue -FunctionName Get-DbaDbAssembly
            }
            $databases = $server.Databases | Where-Object IsAccessible
            if ($__boundDatabase) {
                $databases = $databases | Where-Object Name -in $Database
            }
            foreach ($db in $databases) {
                try {
                    $assemblies = $db.assemblies
                    if ($__boundName) {
                        $assemblies = $assemblies | Where-Object Name -in $Name
                    }
                    foreach ($assembly in $assemblies) {

                        Add-Member -Force -InputObject $assembly -MemberType NoteProperty -Name ComputerName -value $assembly.Parent.Parent.ComputerName
                        Add-Member -Force -InputObject $assembly -MemberType NoteProperty -Name InstanceName -value $assembly.Parent.Parent.ServiceName
                        Add-Member -Force -InputObject $assembly -MemberType NoteProperty -Name SqlInstance -value $assembly.Parent.Parent.DomainInstanceName
                        Add-Member -Force -InputObject $assembly -MemberType NoteProperty -Name Database -value $db.name
                        Add-Member -Force -InputObject $assembly -MemberType NoteProperty -Name DatabaseId -value $db.Id

                        Select-DefaultView -InputObject $assembly -Property ComputerName, InstanceName, SqlInstance, Database, ID, Name, Owner, 'AssemblySecurityLevel as SecurityLevel', CreateDate, IsSystemObject, Version
                    }
                } catch {
                    Stop-Function -Message "Issue pulling assembly information" -Target $assembly -ErrorRecord $_ -Continue -FunctionName Get-DbaDbAssembly
                }
            }
        }
} $SqlInstance $SqlCredential $Database $Name $EnableException $__boundDatabase $__boundName $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
