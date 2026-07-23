#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves schema objects and metadata from databases. Port of public/Get-DbaDbSchema.ps1;
/// the workflow remains a module-scoped PowerShell compatibility hop.
///
/// A process-only port; InputObject is the only ValueFromPipeline parameter (SqlInstance is NOT VFP), so the
/// -SqlInstance path gathers databases via Get-DbaDatabase and appends to $InputObject within the record. The
/// source declares SupportsShouldProcess (ConfirmImpact Low) on the CmdletBinding, so the compiled cmdlet mirrors
/// that on its [Cmdlet] attribute - but the body contains NO $PSCmdlet.ShouldProcess call (this is a read-only
/// Get), so there is NO $__realCmdlet substitution and -WhatIf/-Confirm are accepted but gate nothing (faithful).
/// The process body has ZERO sanctioned edits: no Stop-Function, no Write-Message, no Test-Bound, no interrupt,
/// no accumulator - it ships fully verbatim. Surface pinned by migration/baselines/Get-DbaDbSchema.json
/// (positions 0-5 with two non-positional switches, SupportsShouldProcess ConfirmImpact Low).
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaDbSchema", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Low)]
public sealed class GetDbaDbSchemaCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Position = 0)]
    public DbaInstanceParameter[]? SqlInstance { get; set; }

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The database(s) to process.</summary>
    [Parameter(Position = 2)]
    public string[]? Database { get; set; }

    /// <summary>Filter to the specified schema(s) by name.</summary>
    [Parameter(Position = 3)]
    public string[]? Schema { get; set; }

    /// <summary>Filter to schemas owned by the specified owner(s).</summary>
    [Parameter(Position = 4)]
    public string[]? SchemaOwner { get; set; }

    /// <summary>Include system databases in the -SqlInstance gather.</summary>
    [Parameter]
    public SwitchParameter IncludeSystemDatabases { get; set; }

    /// <summary>Include system schemas in the results.</summary>
    [Parameter]
    public SwitchParameter IncludeSystemSchemas { get; set; }

    /// <summary>Database object(s) piped in from Get-DbaDatabase.</summary>
    [Parameter(ValueFromPipeline = true, Position = 5)]
    public Microsoft.SqlServer.Management.Smo.Database[]? InputObject { get; set; }

    // EnableException is inherited from DbaBaseCmdlet - never redeclared.

    protected override void ProcessRecord()
    {
        if (Interrupted)
            return;

        foreach (PSObject? item in NestedCommand.InvokeScoped(this, ProcessScript,
            SqlInstance, SqlCredential, Database, Schema, SchemaOwner, IncludeSystemDatabases.ToBool(),
            IncludeSystemSchemas.ToBool(), InputObject, EnableException.ToBool(),
            NestedCommand.BoundCommonParameter(this, "Verbose"), NestedCommand.BoundCommonParameter(this, "Debug")))
        {
            if (item?.BaseObject is ErrorRecord nestedError)
            {
                NestedCommand.RemoveDuplicateError(this, nestedError);
                WriteError(nestedError);
                continue;
            }
            WriteObject(item);
        }
    }
    // PS: the process block VERBATIM - zero edits (no Stop-Function, no Write-Message, no Test-Bound, no
    // ShouldProcess call). InputObject is VFP; the -SqlInstance path gathers via Get-DbaDatabase.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Database, $Schema, $SchemaOwner, $IncludeSystemDatabases, $IncludeSystemSchemas, $InputObject, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [string[]]$Database, [string[]]$Schema, [string[]]$SchemaOwner, $IncludeSystemDatabases, $IncludeSystemSchemas, [Microsoft.SqlServer.Management.Smo.Database[]]$InputObject, $EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }


        foreach ($instance in $SqlInstance) {
            $InputObject += Get-DbaDatabase -SqlInstance $instance -SqlCredential $SqlCredential -Database $Database -ExcludeSystem:(-not $IncludeSystemDatabases)
        }

        foreach ($db in $InputObject) {
            $schemaList = $db.Schemas | Where-Object { ($_.IsSystemObject -eq $false) -or ($_.IsSystemObject -eq $IncludeSystemSchemas) } | Where-Object { ($_.Name -in $Schema) -or ($null -eq $Schema) } | Where-Object { ($_.Owner -in $SchemaOwner) -or ($null -eq $SchemaOwner) }

            foreach ($sch in $schemaList) {
                Add-Member -Force -InputObject $sch -MemberType NoteProperty -Name ComputerName -value $db.Parent.ComputerName
                Add-Member -Force -InputObject $sch -MemberType NoteProperty -Name InstanceName -value $db.Parent.ServiceName
                Add-Member -Force -InputObject $sch -MemberType NoteProperty -Name SqlInstance -value $db.Parent.DomainInstanceName
                Add-Member -Force -InputObject $sch -MemberType NoteProperty -Name DatabaseName -value $db.Name
                Add-Member -Force -InputObject $sch -MemberType NoteProperty -Name DatabaseId -value $db.Id
                Select-DefaultView -InputObject $sch -Property ComputerName, InstanceName, SqlInstance, Name, IsSystemObject
            }
        }
} $SqlInstance $SqlCredential $Database $Schema $SchemaOwner $IncludeSystemDatabases $IncludeSystemSchemas $InputObject $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}