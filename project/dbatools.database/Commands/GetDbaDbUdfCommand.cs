#nullable enable

using System;
using System.Collections;
using System.Management.Automation;
using Dataplat.Dbatools.Parameter;

namespace Dataplat.Dbatools.Commands;

/// <summary>
/// Retrieves user-defined function and aggregate objects and metadata from databases. Port of
/// public/Get-DbaDbUdf.ps1; the workflow remains a module-scoped PowerShell compatibility hop.
///
/// A process-only port (SqlInstance is Mandatory ValueFromPipeline, so process fires per record). No accumulator,
/// no interrupt, no Test-Bound, no ShouldProcess. IMPORTANT: ExcludeSystemUdf is a switch consumed as a VALUE
/// (if ($ExcludeSystemUdf) at source line 169), so it is passed as a marshaled bool (ExcludeSystemUdf.ToBool())
/// into an UNTYPED inner hop param - typing it [switch] would exclude it from positional binding and shift the
/// positionally-called scriptblock's args (the switch-in-hop-param law). All other filters (Database/ExcludeDatabase/
/// Schema/ExcludeSchema/Name/ExcludeName) are truthiness-based. The one continue (source 179) is inside foreach ($db)
/// - loop-bound. Edits: -FunctionName Get-DbaDbUdf on the one Stop-Function (-Continue) and three Write-Message.
/// Surface pinned by migration/baselines/Get-DbaDbUdf.json (positions 0-7, ExcludeSystemUdf switch non-positional,
/// SqlInstance Mandatory VFP pos0, no ShouldProcess).
/// </summary>
[Cmdlet(VerbsCommon.Get, "DbaDbUdf")]
public sealed class GetDbaDbUdfCommand : DbaBaseCmdlet
{
    /// <summary>The target SQL Server instance or instances.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, Position = 0)]
    public DbaInstanceParameter[] SqlInstance { get; set; } = null!;

    /// <summary>Login to the target instance using alternative credentials.</summary>
    [Parameter(Position = 1)]
    public PSCredential? SqlCredential { get; set; }

    /// <summary>The database(s) to process.</summary>
    [Parameter(Position = 2)]
    public object[]? Database { get; set; }

    /// <summary>The database(s) to exclude.</summary>
    [Parameter(Position = 3)]
    public object[]? ExcludeDatabase { get; set; }

    /// <summary>Exclude system user-defined functions from the results.</summary>
    [Parameter]
    public SwitchParameter ExcludeSystemUdf { get; set; }

    /// <summary>Filter to UDFs/aggregates in the specified schema(s).</summary>
    [Parameter(Position = 4)]
    public string[]? Schema { get; set; }

    /// <summary>Exclude UDFs/aggregates in the specified schema(s).</summary>
    [Parameter(Position = 5)]
    public string[]? ExcludeSchema { get; set; }

    /// <summary>Filter to the specified UDF/aggregate(s) by name.</summary>
    [Parameter(Position = 6)]
    public string[]? Name { get; set; }

    /// <summary>Exclude the specified UDF/aggregate(s) by name.</summary>
    [Parameter(Position = 7)]
    public string[]? ExcludeName { get; set; }

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
            SqlInstance, SqlCredential, Database, ExcludeDatabase, ExcludeSystemUdf.ToBool(), Schema, ExcludeSchema,
            Name, ExcludeName, EnableException.ToBool(), BoundCommonParameter("Verbose"), BoundCommonParameter("Debug"));
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
    // PS: the process block VERBATIM. Edits: -FunctionName Get-DbaDbUdf on the one Stop-Function (-Continue) and
    // three Write-Message. $ExcludeSystemUdf arrives as a marshaled bool (used via if ($ExcludeSystemUdf)); its
    // inner param is UNTYPED to keep positional binding intact. The one continue is inside foreach ($db) - loop-bound.
    private const string ProcessScript = """
param($SqlInstance, $SqlCredential, $Database, $ExcludeDatabase, $ExcludeSystemUdf, $Schema, $ExcludeSchema, $Name, $ExcludeName, $EnableException, $__boundVerbose, $__boundDebug)
$__commonParameters = @{}
if ($null -ne $__boundVerbose) { $__commonParameters.Verbose = [bool]$__boundVerbose }
if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -lt 7) { $__commonParameters.Debug = [bool]$__boundDebug }
$__dbatoolsModule = Get-Module -Name dbatools | Where-Object ModuleType -eq "Script" | Select-Object -First 1
& $__dbatoolsModule {
    [CmdletBinding()]
    param([Dataplat.Dbatools.Parameter.DbaInstanceParameter[]]$SqlInstance, [PSCredential]$SqlCredential, [object[]]$Database, [object[]]$ExcludeDatabase, $ExcludeSystemUdf, [string[]]$Schema, [string[]]$ExcludeSchema, [string[]]$Name, [string[]]$ExcludeName, $EnableException, $__boundVerbose, $__boundDebug)
    if ($null -ne $__boundDebug -and $PSVersionTable.PSVersion.Major -ge 7) { $DebugPreference = $(if ($__boundDebug) { "Continue" } else { "SilentlyContinue" }) }

        foreach ($instance in $SqlInstance) {
            try {
                $server = Connect-DbaInstance -SqlInstance $instance -SqlCredential $SqlCredential
            } catch {
                Stop-Function -Message "Failure" -Category ConnectionError -ErrorRecord $_ -Target $instance -FunctionName Get-DbaDbUdf -Continue
            }

            $databases = $server.Databases | Where-Object IsAccessible

            if ($Database) {
                $databases = $databases | Where-Object Name -In $Database
            }
            if ($ExcludeDatabase) {
                $databases = $databases | Where-Object Name -NotIn $ExcludeDatabase
            }

            foreach ($db in $databases) {

                # Let the SMO read all properties referenced in this command for all user defined functions in the database in one query.
                # Downside: If some other properties were already read outside of this command in the used SMO, they are cleared.
                try {
                    $db.UserDefinedFunctions.ClearAndInitialize('', [string[]]('Schema', 'Name', 'CreateDate', 'DateLastModified', 'DataType', 'IsSystemObject'))
                } catch {
                    Write-Message -Level Verbose -Message "ClearAndInitialize failed: $_" -FunctionName Get-DbaDbUdf -ModuleName "dbatools"
                }

                # UserDefinedAggregates don't have IsSystemObject property, so initialize separately
                try {
                    $db.UserDefinedAggregates.ClearAndInitialize('', [string[]]('Schema', 'Name', 'CreateDate', 'DateLastModified', 'DataType'))
                } catch {
                    Write-Message -Level Verbose -Message "ClearAndInitialize failed: $_" -FunctionName Get-DbaDbUdf -ModuleName "dbatools"
                }

                $userDefinedFunctions = $db.UserDefinedFunctions

                if ($ExcludeSystemUdf) {
                    $userDefinedFunctions = $userDefinedFunctions | Where-Object IsSystemObject -eq $false
                }

                # Combine UserDefinedFunctions and UserDefinedAggregates
                # UserDefinedAggregates are always user-created (no system aggregates exist)
                $userDefinedFunctions = @($userDefinedFunctions) + @($db.UserDefinedAggregates)

                if (!$userDefinedFunctions -or $userDefinedFunctions.Count -eq 0) {
                    Write-Message -Message "No User Defined Functions or Aggregates exist in the $db database on $instance" -Target $db -Level Verbose -FunctionName Get-DbaDbUdf -ModuleName "dbatools"
                    continue
                }

                if ($Schema) {
                    $userDefinedFunctions = $userDefinedFunctions | Where-Object Schema -in $Schema
                }

                if ($ExcludeSchema) {
                    $userDefinedFunctions = $userDefinedFunctions | Where-Object Schema -notin $ExcludeSchema
                }

                if ($Name) {
                    $userDefinedFunctions = $userDefinedFunctions | Where-Object Name -in $Name
                }

                if ($ExcludeName) {
                    $userDefinedFunctions = $userDefinedFunctions | Where-Object Name -notin $ExcludeName
                }

                $userDefinedFunctions | ForEach-Object {

                    Add-Member -Force -InputObject $_ -MemberType NoteProperty -Name ComputerName -Value $server.ComputerName
                    Add-Member -Force -InputObject $_ -MemberType NoteProperty -Name InstanceName -Value $server.ServiceName
                    Add-Member -Force -InputObject $_ -MemberType NoteProperty -Name SqlInstance -Value $server.DomainInstanceName
                    Add-Member -Force -InputObject $_ -MemberType NoteProperty -Name Database -Value $db.Name

                    Select-DefaultView -InputObject $_ -Property ComputerName, InstanceName, SqlInstance, Database, Schema, CreateDate, DateLastModified, Name, DataType
                }
            }
        }
} $SqlInstance $SqlCredential $Database $ExcludeDatabase $ExcludeSystemUdf $Schema $ExcludeSchema $Name $ExcludeName $EnableException $__boundVerbose $__boundDebug @__commonParameters 3>&1 2>&1
""";
}
